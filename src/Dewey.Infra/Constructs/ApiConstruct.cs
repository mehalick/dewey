using Amazon.CDK;
using Amazon.CDK.AWS.Cognito;
using Amazon.CDK.AWS.DynamoDB;
using Amazon.CDK.AWS.Lambda;
using Amazon.CDK.AWS.S3;
using Constructs;

namespace Dewey.Infra.Constructs;

public sealed class ApiConstructProps
{
    public Table UsersTable { get; init; }
    public Table BooksTable { get; init; }
    public Table SessionsTable { get; init; }
    public Bucket CoversBucket { get; init; }
    public UserPool UserPool { get; init; }
    public UserPoolClient UserPoolClient { get; init; }
}

public sealed class ApiConstruct : Construct
{
    public Function Function { get; }
    public FunctionUrl FunctionUrl { get; }

    public ApiConstruct(Construct scope, string id, ApiConstructProps props) : base(scope, id)
    {
        // Code asset path is populated by the CI build (publish output of Dewey.Api).
        // For local synth without a build, fall back to an inline placeholder.
        var codePath = System.Environment.GetEnvironmentVariable("DEWEY_API_PUBLISH_DIR");
        Code code = !string.IsNullOrEmpty(codePath)
            ? Code.FromAsset(codePath)
            : Code.FromInline("placeholder");

        Function = new Function(this, "Handler", new FunctionProps
        {
            Runtime = Runtime.PROVIDED_AL2023,
            Architecture = Architecture.ARM_64,
            Handler = "bootstrap",
            Code = code,
            MemorySize = 512,
            Timeout = Duration.Seconds(10),
            Tracing = Tracing.ACTIVE,
            Environment = new System.Collections.Generic.Dictionary<string, string>
            {
                ["DEWEY_USER_POOL_ID"] = props.UserPool.UserPoolId,
                ["DEWEY_USER_POOL_CLIENT_ID"] = props.UserPoolClient.UserPoolClientId,
                ["DEWEY_AWS_REGION"] = Stack.Of(this).Region,
                ["DEWEY_USERS_TABLE"] = props.UsersTable.TableName,
                ["DEWEY_BOOKS_TABLE"] = props.BooksTable.TableName,
                ["DEWEY_SESSIONS_TABLE"] = props.SessionsTable.TableName,
                ["DEWEY_COVERS_BUCKET"] = props.CoversBucket.BucketName,
                ["DEWEY_GOOGLE_BOOKS_KEY"] = System.Environment.GetEnvironmentVariable("DEWEY_GOOGLE_BOOKS_KEY") ?? "",
            },
        });

        props.UsersTable.GrantReadWriteData(Function);
        props.BooksTable.GrantReadWriteData(Function);
        props.SessionsTable.GrantReadWriteData(Function);
        props.CoversBucket.GrantReadWrite(Function);

        FunctionUrl = Function.AddFunctionUrl(new FunctionUrlOptions
        {
            AuthType = FunctionUrlAuthType.NONE,
        });
    }
}
