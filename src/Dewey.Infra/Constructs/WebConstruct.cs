using Amazon.CDK;
using Amazon.CDK.AWS.CloudFront;
using Amazon.CDK.AWS.CloudFront.Origins;
using LambdaFunction = Amazon.CDK.AWS.Lambda.Function;
using Amazon.CDK.AWS.Lambda;
using Amazon.CDK.AWS.S3;
using Amazon.CDK.AWS.S3.Deployment;
using Constructs;

namespace Dewey.Infra.Constructs;

public sealed class WebConstructProps
{
    public LambdaFunction ApiFunction { get; init; }
    public FunctionUrl ApiFunctionUrl { get; init; }
    public Bucket CoversBucket { get; init; }
    public string CognitoRegion { get; init; }
    public string UserPoolClientId { get; init; }
}

public sealed class WebConstruct : Construct
{
    public Bucket SiteBucket { get; }
    public Distribution Distribution { get; }

    public WebConstruct(Construct scope, string id, WebConstructProps props) : base(scope, id)
    {
        SiteBucket = new Bucket(this, "Site", new BucketProps
        {
            BlockPublicAccess = BlockPublicAccess.BLOCK_ALL,
            Encryption = BucketEncryption.S3_MANAGED,
            RemovalPolicy = RemovalPolicy.RETAIN,
        });

        var apiOrigin = new FunctionUrlOrigin(props.ApiFunctionUrl);

        Distribution = new Distribution(this, "Cdn", new DistributionProps
        {
            DefaultRootObject = "index.html",
            DefaultBehavior = new BehaviorOptions
            {
                Origin = S3BucketOrigin.WithOriginAccessControl(SiteBucket),
                ViewerProtocolPolicy = ViewerProtocolPolicy.REDIRECT_TO_HTTPS,
            },
            AdditionalBehaviors = new System.Collections.Generic.Dictionary<string, IBehaviorOptions>
            {
                ["/api/*"] = new BehaviorOptions
                {
                    Origin = apiOrigin,
                    ViewerProtocolPolicy = ViewerProtocolPolicy.REDIRECT_TO_HTTPS,
                    AllowedMethods = AllowedMethods.ALLOW_ALL,
                    CachePolicy = CachePolicy.CACHING_DISABLED,
                    OriginRequestPolicy = OriginRequestPolicy.ALL_VIEWER_EXCEPT_HOST_HEADER,
                },
                ["/covers/*"] = new BehaviorOptions
                {
                    Origin = S3BucketOrigin.WithOriginAccessControl(props.CoversBucket),
                    ViewerProtocolPolicy = ViewerProtocolPolicy.REDIRECT_TO_HTTPS,
                    CachePolicy = CachePolicy.CACHING_OPTIMIZED,
                },
            },
            ErrorResponses = new[]
            {
                new ErrorResponse
                {
                    HttpStatus = 403,
                    ResponseHttpStatus = 200,
                    ResponsePagePath = "/index.html",
                },
                new ErrorResponse
                {
                    HttpStatus = 404,
                    ResponseHttpStatus = 200,
                    ResponsePagePath = "/index.html",
                },
            },
        });

        var publishDir = System.Environment.GetEnvironmentVariable("DEWEY_WEB_PUBLISH_DIR");
        if (!string.IsNullOrEmpty(publishDir))
        {
            // Write a runtime appsettings.json overlaying the static one with
            // the deployed Cognito client + region.
            var appsettings = $@"{{""Cognito"":{{""Region"":""{props.CognitoRegion}"",""UserPoolClientId"":""{props.UserPoolClientId}""}},""Api"":{{""BaseAddress"":""/""}}}}";

            new BucketDeployment(this, "DeploySite", new BucketDeploymentProps
            {
                Sources = new[]
                {
                    Source.Asset(publishDir),
                    Source.JsonData("appsettings.json", new System.Collections.Generic.Dictionary<string, object>
                    {
                        ["Cognito"] = new System.Collections.Generic.Dictionary<string, object>
                        {
                            ["Region"] = props.CognitoRegion,
                            ["UserPoolClientId"] = props.UserPoolClientId,
                        },
                        ["Api"] = new System.Collections.Generic.Dictionary<string, object>
                        {
                            ["BaseAddress"] = "/",
                        },
                    }),
                },
                DestinationBucket = SiteBucket,
                Distribution = Distribution,
                DistributionPaths = new[] { "/*" },
            });
            _ = appsettings;
        }

        new CfnOutput(this, "DistributionUrl", new CfnOutputProps
        {
            Value = $"https://{Distribution.DistributionDomainName}",
        });
    }
}
