using Amazon.CDK;
using Amazon.CDK.AWS.Cognito;
using Constructs;

namespace Dewey.Infra.Constructs;

public sealed class AuthConstruct : Construct
{
    public UserPool UserPool { get; }
    public UserPoolClient UserPoolClient { get; }

    public AuthConstruct(Construct scope, string id) : base(scope, id)
    {
        // M1 skeleton: minimal user pool. Magic-link custom-auth triggers
        // (DefineAuthChallenge / CreateAuthChallenge / VerifyAuthChallengeResponse)
        // are wired in M2.
        UserPool = new UserPool(this, "UserPool", new UserPoolProps
        {
            SignInAliases = new SignInAliases { Email = true },
            SelfSignUpEnabled = true,
            RemovalPolicy = RemovalPolicy.RETAIN,
        });

        UserPoolClient = UserPool.AddClient("WebClient", new UserPoolClientOptions
        {
            GenerateSecret = false,
            AuthFlows = new AuthFlow { Custom = true },
        });
    }
}
