using Amazon.CDK;
using Amazon.CDK.AWS.DynamoDB;
using Amazon.CDK.AWS.S3;
using Constructs;

namespace Dewey.Infra.Constructs;

public sealed class DataConstruct : Construct
{
    public Table UsersTable { get; }
    public Table BooksTable { get; }
    public Table SessionsTable { get; }
    public Bucket CoversBucket { get; }

    public DataConstruct(Construct scope, string id) : base(scope, id)
    {
        UsersTable = new Table(this, "Users", new TableProps
        {
            PartitionKey = new Amazon.CDK.AWS.DynamoDB.Attribute { Name = "userId", Type = AttributeType.STRING },
            BillingMode = BillingMode.PAY_PER_REQUEST,
            PointInTimeRecoverySpecification = new PointInTimeRecoverySpecification { PointInTimeRecoveryEnabled = true },
            RemovalPolicy = RemovalPolicy.RETAIN,
        });

        BooksTable = new Table(this, "Books", new TableProps
        {
            PartitionKey = new Amazon.CDK.AWS.DynamoDB.Attribute { Name = "userId", Type = AttributeType.STRING },
            SortKey = new Amazon.CDK.AWS.DynamoDB.Attribute { Name = "bookId", Type = AttributeType.STRING },
            BillingMode = BillingMode.PAY_PER_REQUEST,
            PointInTimeRecoverySpecification = new PointInTimeRecoverySpecification { PointInTimeRecoveryEnabled = true },
            RemovalPolicy = RemovalPolicy.RETAIN,
        });
        BooksTable.AddGlobalSecondaryIndex(new GlobalSecondaryIndexProps
        {
            IndexName = "ByLatestSession",
            PartitionKey = new Amazon.CDK.AWS.DynamoDB.Attribute { Name = "userId", Type = AttributeType.STRING },
            SortKey = new Amazon.CDK.AWS.DynamoDB.Attribute { Name = "latestSessionAt", Type = AttributeType.STRING },
        });

        SessionsTable = new Table(this, "ReadingSessions", new TableProps
        {
            PartitionKey = new Amazon.CDK.AWS.DynamoDB.Attribute { Name = "userBookKey", Type = AttributeType.STRING },
            SortKey = new Amazon.CDK.AWS.DynamoDB.Attribute { Name = "occurredAtSessionId", Type = AttributeType.STRING },
            BillingMode = BillingMode.PAY_PER_REQUEST,
            PointInTimeRecoverySpecification = new PointInTimeRecoverySpecification { PointInTimeRecoveryEnabled = true },
            RemovalPolicy = RemovalPolicy.RETAIN,
        });

        CoversBucket = new Bucket(this, "Covers", new BucketProps
        {
            BlockPublicAccess = BlockPublicAccess.BLOCK_ALL,
            Encryption = BucketEncryption.S3_MANAGED,
            RemovalPolicy = RemovalPolicy.RETAIN,
        });
    }
}
