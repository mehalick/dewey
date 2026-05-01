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
    public Bucket CoversBucket { get; init; }
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

        var apiOrigin = new FunctionUrlOrigin(props.ApiFunction.AddFunctionUrl(new FunctionUrlOptions
        {
            AuthType = FunctionUrlAuthType.NONE,
        }));

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
            new BucketDeployment(this, "DeploySite", new BucketDeploymentProps
            {
                Sources = new[] { Source.Asset(publishDir) },
                DestinationBucket = SiteBucket,
                Distribution = Distribution,
                DistributionPaths = new[] { "/*" },
            });
        }

        new CfnOutput(this, "DistributionUrl", new CfnOutputProps
        {
            Value = $"https://{Distribution.DistributionDomainName}",
        });
    }
}
