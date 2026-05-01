using Amazon.CDK;
using Amazon.CDK.AWS.Cognito;
using Amazon.CDK.AWS.Lambda;
using Amazon.CDK.AWS.Logs;
using Constructs;

namespace Dewey.Infra.Constructs;

public sealed class AuthConstruct : Construct
{
    public UserPool UserPool { get; }
    public UserPoolClient UserPoolClient { get; }
    public Function TriggersFunction { get; }

    public AuthConstruct(Construct scope, string id) : base(scope, id)
    {
        var triggersPath = System.Environment.GetEnvironmentVariable("DEWEY_AUTH_TRIGGERS_PUBLISH_DIR");
        Code triggersCode = !string.IsNullOrEmpty(triggersPath)
            ? Code.FromAsset(triggersPath)
            : Code.FromInline("placeholder");

        var triggersLogGroup = new Amazon.CDK.AWS.Logs.LogGroup(this, "TriggersLogGroup", new Amazon.CDK.AWS.Logs.LogGroupProps
        {
            Retention = RetentionDays.TWO_WEEKS,
            RemovalPolicy = RemovalPolicy.DESTROY,
        });

        TriggersFunction = new Function(this, "Triggers", new FunctionProps
        {
            Runtime = Runtime.PROVIDED_AL2023,
            Architecture = Architecture.ARM_64,
            Handler = "bootstrap",
            Code = triggersCode,
            MemorySize = 256,
            Timeout = Duration.Seconds(5),
            Tracing = Tracing.ACTIVE,
            LoggingFormat = LoggingFormat.JSON,
            ApplicationLogLevelV2 = ApplicationLogLevel.INFO,
            SystemLogLevelV2 = SystemLogLevel.INFO,
            LogGroup = triggersLogGroup,
        });

        UserPool = new UserPool(this, "UserPool", new UserPoolProps
        {
            SignInAliases = new SignInAliases { Email = true },
            AutoVerify = new AutoVerifiedAttrs { Email = true },
            SelfSignUpEnabled = true,
            // Passwordless flow uses a server-generated random password during
            // SignUp; relax the policy so client-side random passwords are accepted.
            PasswordPolicy = new PasswordPolicy
            {
                MinLength = 8,
                RequireLowercase = false,
                RequireUppercase = false,
                RequireDigits = false,
                RequireSymbols = false,
            },
            LambdaTriggers = new UserPoolTriggers
            {
                PreSignUp = TriggersFunction,
                DefineAuthChallenge = TriggersFunction,
                CreateAuthChallenge = TriggersFunction,
                VerifyAuthChallengeResponse = TriggersFunction,
            },
            RemovalPolicy = RemovalPolicy.RETAIN,
        });

        UserPoolClient = UserPool.AddClient("WebClient", new UserPoolClientOptions
        {
            GenerateSecret = false,
            AuthFlows = new AuthFlow
            {
                Custom = true,
                UserSrp = true,
            },
            PreventUserExistenceErrors = true,
            IdTokenValidity = Duration.Hours(1),
            AccessTokenValidity = Duration.Hours(1),
            RefreshTokenValidity = Duration.Days(30),
        });

        new CfnOutput(this, "UserPoolId", new CfnOutputProps { Value = UserPool.UserPoolId });
        new CfnOutput(this, "UserPoolClientId", new CfnOutputProps { Value = UserPoolClient.UserPoolClientId });
    }
}
