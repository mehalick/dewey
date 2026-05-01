using Amazon.CDK;
using Constructs;
using Dewey.Infra.Constructs;

namespace Dewey.Infra;

public sealed class DeweyStack : Stack
{
    internal DeweyStack(Construct scope, string id, IStackProps props = null)
        : base(scope, id, props)
    {
        var data = new DataConstruct(this, "Data");
        var auth = new AuthConstruct(this, "Auth");
        var api = new ApiConstruct(this, "Api", new ApiConstructProps
        {
            UsersTable = data.UsersTable,
            BooksTable = data.BooksTable,
            SessionsTable = data.SessionsTable,
            CoversBucket = data.CoversBucket,
            UserPool = auth.UserPool,
            UserPoolClient = auth.UserPoolClient,
        });
        var web = new WebConstruct(this, "Web", new WebConstructProps
        {
            ApiFunction = api.Function,
            CoversBucket = data.CoversBucket,
            CognitoRegion = Region,
            UserPoolClientId = auth.UserPoolClient.UserPoolClientId,
        });
        new ObservabilityConstruct(this, "Observability", new ObservabilityConstructProps
        {
            ApiFunction = api.Function,
            AuthTriggersFunction = auth.TriggersFunction,
            UsersTable = data.UsersTable,
            BooksTable = data.BooksTable,
            SessionsTable = data.SessionsTable,
            Distribution = web.Distribution,
        });
    }
}
