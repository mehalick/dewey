using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Dewey.Web.Auth;

// Talks to Cognito User Pools "JSON service" over plain HTTPS using the
// AWSCognitoIdentityProviderService.* x-amz-target headers. The unauthenticated
// flows we need (SignUp, InitiateAuth, RespondToAuthChallenge) do NOT require
// SigV4 signing, so this avoids pulling in the AWS SDK on the WASM client.
public sealed class CognitoClient
{
    private readonly HttpClient _http;
    private readonly CognitoOptions _opts;

    public CognitoClient(HttpClient http, CognitoOptions opts)
    {
        _http = http;
        _opts = opts;
    }

    private string Endpoint => $"https://cognito-idp.{_opts.Region}.amazonaws.com/";

    private async Task<TRes> CallAsync<TReq, TRes>(string action, TReq body, JsonTypeInfo<TReq> reqInfo, JsonTypeInfo<TRes> resInfo)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = JsonContent.Create(body, reqInfo),
        };
        req.Headers.Add("X-Amz-Target", $"AWSCognitoIdentityProviderService.{action}");
        req.Content!.Headers.ContentType = new("application/x-amz-json-1.1");

        using var res = await _http.SendAsync(req);
        if (!res.IsSuccessStatusCode)
        {
            var error = await res.Content.ReadAsStringAsync();
            throw new CognitoException($"{action} failed ({(int)res.StatusCode}): {error}");
        }

        var parsed = await res.Content.ReadFromJsonAsync(resInfo);
        return parsed ?? throw new CognitoException($"{action}: empty response");
    }

    public Task<SignUpResponse> SignUpAsync(string email, string password)
        => CallAsync(
            "SignUp",
            new SignUpRequest(_opts.UserPoolClientId, email, password,
                [new AttributeType("email", email)]),
            CognitoJsonContext.Default.SignUpRequest,
            CognitoJsonContext.Default.SignUpResponse);

    public Task<InitiateAuthResponse> InitiateCustomAuthAsync(string email)
        => CallAsync(
            "InitiateAuth",
            new InitiateAuthRequest("CUSTOM_AUTH", _opts.UserPoolClientId,
                new() { ["USERNAME"] = email }),
            CognitoJsonContext.Default.InitiateAuthRequest,
            CognitoJsonContext.Default.InitiateAuthResponse);

    public Task<InitiateAuthResponse> RespondToCustomChallengeAsync(string email, string code, string session)
        => CallAsync(
            "RespondToAuthChallenge",
            new RespondToAuthChallengeRequest("CUSTOM_CHALLENGE", _opts.UserPoolClientId,
                new() { ["USERNAME"] = email, ["ANSWER"] = code },
                session),
            CognitoJsonContext.Default.RespondToAuthChallengeRequest,
            CognitoJsonContext.Default.InitiateAuthResponse);

    public static string GenerateRandomPassword()
    {
        // 32 bytes of randomness, base64 — safely passes the relaxed user-pool policy.
        var bytes = new byte[24];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes) + "Aa1!";
    }
}

public sealed class CognitoException(string message) : Exception(message);

public sealed record AttributeType(
    [property: JsonPropertyName("Name")] string Name,
    [property: JsonPropertyName("Value")] string Value);

public sealed record SignUpRequest(
    [property: JsonPropertyName("ClientId")] string ClientId,
    [property: JsonPropertyName("Username")] string Username,
    [property: JsonPropertyName("Password")] string Password,
    [property: JsonPropertyName("UserAttributes")] AttributeType[] UserAttributes);

public sealed record SignUpResponse(
    [property: JsonPropertyName("UserConfirmed")] bool UserConfirmed,
    [property: JsonPropertyName("UserSub")] string? UserSub);

public sealed record InitiateAuthRequest(
    [property: JsonPropertyName("AuthFlow")] string AuthFlow,
    [property: JsonPropertyName("ClientId")] string ClientId,
    [property: JsonPropertyName("AuthParameters")] Dictionary<string, string> AuthParameters);

public sealed record RespondToAuthChallengeRequest(
    [property: JsonPropertyName("ChallengeName")] string ChallengeName,
    [property: JsonPropertyName("ClientId")] string ClientId,
    [property: JsonPropertyName("ChallengeResponses")] Dictionary<string, string> ChallengeResponses,
    [property: JsonPropertyName("Session")] string? Session);

public sealed record InitiateAuthResponse(
    [property: JsonPropertyName("ChallengeName")] string? ChallengeName,
    [property: JsonPropertyName("Session")] string? Session,
    [property: JsonPropertyName("ChallengeParameters")] Dictionary<string, string>? ChallengeParameters,
    [property: JsonPropertyName("AuthenticationResult")] AuthenticationResult? AuthenticationResult);

public sealed record AuthenticationResult(
    [property: JsonPropertyName("IdToken")] string IdToken,
    [property: JsonPropertyName("AccessToken")] string AccessToken,
    [property: JsonPropertyName("RefreshToken")] string? RefreshToken,
    [property: JsonPropertyName("ExpiresIn")] int ExpiresIn,
    [property: JsonPropertyName("TokenType")] string TokenType);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.Unspecified)]
[JsonSerializable(typeof(SignUpRequest))]
[JsonSerializable(typeof(SignUpResponse))]
[JsonSerializable(typeof(InitiateAuthRequest))]
[JsonSerializable(typeof(InitiateAuthResponse))]
[JsonSerializable(typeof(RespondToAuthChallengeRequest))]
[JsonSerializable(typeof(AuthenticationResult))]
[JsonSerializable(typeof(AttributeType))]
internal partial class CognitoJsonContext : JsonSerializerContext;
