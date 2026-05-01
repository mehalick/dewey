using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Amazon.Lambda.Core;
using Amazon.Lambda.RuntimeSupport;
using Amazon.Lambda.Serialization.SystemTextJson;

namespace Dewey.AuthTriggers;

// Single Lambda fronts four Cognito triggers (PreSignUp, DefineAuthChallenge,
// CreateAuthChallenge, VerifyAuthChallengeResponse). The event envelope shape is
// consistent across triggers; we mutate the `response` object based on
// `triggerSource` and return the whole event back to Cognito.
//
// AOT note: we deliberately use JsonNode rather than typed records because the
// `request`/`response` shapes vary per trigger. JsonNode works under AOT without
// a JsonSerializerContext.
public static class Function
{
    private const string ChallengeName = "CUSTOM_CHALLENGE";
    private const int OtpDigits = 6;

    public static async Task Main()
    {
        Func<JsonNode, ILambdaContext, Task<JsonNode>> handler = Handle;
        await LambdaBootstrapBuilder.Create(handler, new SourceGeneratorLambdaJsonSerializer<TriggerJsonContext>())
            .Build()
            .RunAsync();
    }

    public static Task<JsonNode> Handle(JsonNode evt, ILambdaContext ctx)
    {
        var triggerSource = evt["triggerSource"]?.GetValue<string>() ?? string.Empty;
        ctx.Logger.LogInformation($"Cognito trigger: {triggerSource}");

        switch (triggerSource)
        {
            case "PreSignUp_SignUp":
            case "PreSignUp_AdminCreateUser":
                HandlePreSignUp(evt);
                break;
            case "DefineAuthChallenge_Authentication":
                HandleDefineAuthChallenge(evt);
                break;
            case "CreateAuthChallenge_Authentication":
                HandleCreateAuthChallenge(evt, ctx);
                break;
            case "VerifyAuthChallengeResponse_Authentication":
                HandleVerifyAuthChallenge(evt);
                break;
            default:
                ctx.Logger.LogWarning($"Unhandled trigger source: {triggerSource}");
                break;
        }

        return Task.FromResult(evt);
    }

    private static void HandlePreSignUp(JsonNode evt)
    {
        // Auto-confirm new users; passwordless flow doesn't use email-link verification.
        var response = EnsureObject(evt, "response");
        response["autoConfirmUser"] = true;
        response["autoVerifyEmail"] = true;
    }

    private static void HandleDefineAuthChallenge(JsonNode evt)
    {
        var request = evt["request"]?.AsObject();
        var session = request?["session"]?.AsArray();
        var response = EnsureObject(evt, "response");

        if (session is null || session.Count == 0)
        {
            // First step: issue our custom challenge.
            response["challengeName"] = ChallengeName;
            response["issueTokens"] = false;
            response["failAuthentication"] = false;
            return;
        }

        // Last attempt's result.
        var last = session[^1]?.AsObject();
        var lastName = last?["challengeName"]?.GetValue<string>();
        var lastResult = last?["challengeResult"]?.GetValue<bool>() ?? false;

        if (lastName == ChallengeName && lastResult)
        {
            response["issueTokens"] = true;
            response["failAuthentication"] = false;
            return;
        }

        // Allow up to 3 attempts before failing the flow.
        if (session.Count >= 3)
        {
            response["issueTokens"] = false;
            response["failAuthentication"] = true;
            return;
        }

        response["challengeName"] = ChallengeName;
        response["issueTokens"] = false;
        response["failAuthentication"] = false;
    }

    private static void HandleCreateAuthChallenge(JsonNode evt, ILambdaContext ctx)
    {
        var request = evt["request"]?.AsObject();
        var challengeName = request?["challengeName"]?.GetValue<string>();
        if (challengeName != ChallengeName) return;

        // Reuse the OTP across retries within the same auth session.
        var session = request?["session"]?.AsArray();
        string? otp = null;
        if (session is { Count: > 0 })
        {
            for (var i = session.Count - 1; i >= 0; i--)
            {
                var meta = session[i]?.AsObject()?["challengeMetadata"]?.GetValue<string>();
                if (!string.IsNullOrEmpty(meta) && meta.StartsWith("CODE-", StringComparison.Ordinal))
                {
                    otp = meta.AsSpan(5).ToString();
                    break;
                }
            }
        }
        otp ??= GenerateOtp();

        var email = request?["userAttributes"]?["email"]?.GetValue<string>() ?? "(unknown)";

        var response = EnsureObject(evt, "response");
        response["publicChallengeParameters"] = new JsonObject { ["email"] = email };
        response["privateChallengeParameters"] = new JsonObject { ["code"] = otp };
        response["challengeMetadata"] = $"CODE-{otp}";

        // M2 dev-only delivery: OTP is logged to CloudWatch. Cognito's CUSTOM_AUTH
        // flow does NOT auto-send email — productionizing requires either
        // (a) calling SES from this trigger, or (b) wiring a CustomEmailSender
        // trigger. Tracked for M3+.
        ctx.Logger.LogInformation($"[DEV-OTP] email={email} code={otp}");
    }

    private static void HandleVerifyAuthChallenge(JsonNode evt)
    {
        var request = evt["request"]?.AsObject();
        var expected = request?["privateChallengeParameters"]?["code"]?.GetValue<string>();
        var supplied = request?["challengeAnswer"]?.GetValue<string>();

        var response = EnsureObject(evt, "response");
        response["answerCorrect"] =
            !string.IsNullOrEmpty(expected) &&
            !string.IsNullOrEmpty(supplied) &&
            CryptographicEquals(expected, supplied);
    }

    private static string GenerateOtp()
    {
        Span<byte> buf = stackalloc byte[4];
        System.Security.Cryptography.RandomNumberGenerator.Fill(buf);
        var n = BitConverter.ToUInt32(buf) % (uint)Math.Pow(10, OtpDigits);
        return n.ToString(System.Globalization.CultureInfo.InvariantCulture).PadLeft(OtpDigits, '0');
    }

    private static bool CryptographicEquals(string a, string b)
    {
        if (a.Length != b.Length) return false;
        var diff = 0;
        for (var i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
        return diff == 0;
    }

    private static JsonObject EnsureObject(JsonNode evt, string name)
    {
        var obj = evt.AsObject();
        if (obj[name] is JsonObject existing) return existing;
        var created = new JsonObject();
        obj[name] = created;
        return created;
    }
}

[JsonSerializable(typeof(JsonNode))]
[JsonSerializable(typeof(JsonObject))]
[JsonSerializable(typeof(JsonArray))]
internal partial class TriggerJsonContext : JsonSerializerContext;
