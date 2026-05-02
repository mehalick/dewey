using Amazon.CDK;
using Amazon.CDK.AWS.CloudFront;
using Amazon.CDK.AWS.CloudWatch;
using Amazon.CDK.AWS.CloudWatch.Actions;
using Amazon.CDK.AWS.DynamoDB;
using LambdaFunction = Amazon.CDK.AWS.Lambda.Function;
using Amazon.CDK.AWS.Lambda;
using Amazon.CDK.AWS.SNS;
using Amazon.CDK.AWS.SNS.Subscriptions;
using Constructs;

namespace Dewey.Infra.Constructs;

public sealed class ObservabilityConstructProps
{
    public LambdaFunction ApiFunction { get; init; }
    public LambdaFunction AuthTriggersFunction { get; init; }
    public Table UsersTable { get; init; }
    public Table BooksTable { get; init; }
    public Table SessionsTable { get; init; }
    public Distribution Distribution { get; init; }
}

public sealed class ObservabilityConstruct : Construct
{
    public Topic AlertTopic { get; }

    public ObservabilityConstruct(Construct scope, string id, ObservabilityConstructProps props) : base(scope, id)
    {
        AlertTopic = new Topic(this, "Alerts", new TopicProps
        {
            DisplayName = "Dewey alerts",
        });

        var alertEmail = System.Environment.GetEnvironmentVariable("DEWEY_ALERT_EMAIL");
        if (!string.IsNullOrEmpty(alertEmail))
        {
            AlertTopic.AddSubscription(new EmailSubscription(alertEmail));
        }

        var snsAction = new SnsAction(AlertTopic);

        // ---- Lambda alarms ----
        AddAlarm("ApiErrorRate",
            props.ApiFunction.MetricErrors(new MetricOptions { Period = Duration.Minutes(5), Statistic = "Sum" }),
            threshold: 5, evalPeriods: 1, snsAction);

        AddAlarm("ApiP99Latency",
            props.ApiFunction.MetricDuration(new MetricOptions { Period = Duration.Minutes(5), Statistic = "p99" }),
            threshold: 3000, evalPeriods: 2, snsAction);

        AddAlarm("ApiThrottles",
            props.ApiFunction.MetricThrottles(new MetricOptions { Period = Duration.Minutes(5), Statistic = "Sum" }),
            threshold: 1, evalPeriods: 1, snsAction);

        AddAlarm("AuthTriggersErrors",
            props.AuthTriggersFunction.MetricErrors(new MetricOptions { Period = Duration.Minutes(5), Statistic = "Sum" }),
            threshold: 1, evalPeriods: 1, snsAction);

        // ---- DynamoDB throttle alarms ----
        foreach (var (name, table) in new[]
        {
            ("Users", props.UsersTable),
            ("Books", props.BooksTable),
            ("Sessions", props.SessionsTable),
        })
        {
            AddAlarm($"Ddb{name}Throttles",
                table.MetricThrottledRequestsForOperations(new OperationsMetricOptions
                {
                    Operations = new[]
                    {
                        Operation.GET_ITEM, Operation.PUT_ITEM, Operation.QUERY,
                        Operation.UPDATE_ITEM, Operation.DELETE_ITEM, Operation.TRANSACT_WRITE_ITEMS,
                    },
                    Period = Duration.Minutes(5),
                    Statistic = "Sum",
                }),
                threshold: 1, evalPeriods: 1, snsAction);

            AddAlarm($"Ddb{name}SystemErrors",
                table.MetricSystemErrorsForOperations(new SystemErrorsForOperationsMetricOptions
                {
                    Operations = new[]
                    {
                        Operation.GET_ITEM, Operation.PUT_ITEM, Operation.QUERY,
                        Operation.UPDATE_ITEM, Operation.DELETE_ITEM, Operation.TRANSACT_WRITE_ITEMS,
                    },
                    Period = Duration.Minutes(5),
                    Statistic = "Sum",
                }),
                threshold: 1, evalPeriods: 1, snsAction);
        }

        // ---- CloudFront 5xx ---- (CloudFront metrics live in us-east-1 only)
        var cfErrorRate = new Metric(new MetricProps
        {
            Namespace = "AWS/CloudFront",
            MetricName = "5xxErrorRate",
            DimensionsMap = new System.Collections.Generic.Dictionary<string, string>
            {
                ["DistributionId"] = props.Distribution.DistributionId,
                ["Region"] = "Global",
            },
            Statistic = "Average",
            Period = Duration.Minutes(5),
            Region = "us-east-1",
        });
        AddAlarm("CloudFront5xxRate", cfErrorRate, threshold: 1.0, evalPeriods: 2, snsAction);

        // ---- Dashboard ----
        new Dashboard(this, "Dashboard", new DashboardProps
        {
            DashboardName = "Dewey",
            Widgets = new[]
            {
                new IWidget[]
                {
                    LambdaWidget("API", props.ApiFunction),
                    LambdaWidget("Auth triggers", props.AuthTriggersFunction),
                },
                new IWidget[]
                {
                    DdbWidget("Books", props.BooksTable),
                    DdbWidget("Sessions", props.SessionsTable),
                },
                new IWidget[]
                {
                    CloudFrontWidget(props.Distribution),
                },
            },
        });
    }

    private void AddAlarm(string id, IMetric metric, double threshold, int evalPeriods, SnsAction action)
    {
        var alarm = new Alarm(this, id, new AlarmProps
        {
            Metric = metric,
            Threshold = threshold,
            EvaluationPeriods = evalPeriods,
            ComparisonOperator = ComparisonOperator.GREATER_THAN_THRESHOLD,
            TreatMissingData = TreatMissingData.NOT_BREACHING,
        });
        alarm.AddAlarmAction(action);
    }

    private static GraphWidget LambdaWidget(string title, LambdaFunction f) => new(new GraphWidgetProps
    {
        Title = $"{title} — invocations / errors / duration",
        Width = 12,
        Left = new[]
        {
            f.MetricInvocations(),
            f.MetricErrors(),
        },
        Right = new[]
        {
            f.MetricDuration(new MetricOptions { Statistic = "p50" }),
            f.MetricDuration(new MetricOptions { Statistic = "p99" }),
        },
    });

    private static GraphWidget DdbWidget(string title, Table t)
    {
        // SuccessfulRequestLatency requires a single Operation dimension per
        // metric, so we emit one metric per operation rather than aggregating.
        var latencyOps = new[]
        {
            Operation.GET_ITEM, Operation.QUERY, Operation.PUT_ITEM,
            Operation.UPDATE_ITEM, Operation.TRANSACT_WRITE_ITEMS,
        };
        var latencyMetrics = latencyOps
            .Select(op => (IMetric)new Metric(new MetricProps
            {
                Namespace = "AWS/DynamoDB",
                MetricName = "SuccessfulRequestLatency",
                DimensionsMap = new System.Collections.Generic.Dictionary<string, string>
                {
                    ["TableName"] = t.TableName,
                    ["Operation"] = op.ToString(),
                },
                Statistic = "p99",
                Period = Duration.Minutes(5),
                Label = op.ToString(),
            }))
            .ToArray();

        return new GraphWidget(new GraphWidgetProps
        {
            Title = $"DDB — {title}",
            Width = 12,
            Left = new[]
            {
                t.MetricConsumedReadCapacityUnits(),
                t.MetricConsumedWriteCapacityUnits(),
            },
            Right = latencyMetrics,
        });
    }

    private static GraphWidget CloudFrontWidget(Distribution d) => new(new GraphWidgetProps
    {
        Title = "CloudFront — requests / 4xx / 5xx",
        Width = 24,
        Left = new[]
        {
            new Metric(new MetricProps { Namespace = "AWS/CloudFront", MetricName = "Requests", DimensionsMap = new System.Collections.Generic.Dictionary<string, string> { ["DistributionId"] = d.DistributionId, ["Region"] = "Global" }, Region = "us-east-1", Statistic = "Sum" }),
        },
        Right = new[]
        {
            new Metric(new MetricProps { Namespace = "AWS/CloudFront", MetricName = "4xxErrorRate", DimensionsMap = new System.Collections.Generic.Dictionary<string, string> { ["DistributionId"] = d.DistributionId, ["Region"] = "Global" }, Region = "us-east-1", Statistic = "Average" }),
            new Metric(new MetricProps { Namespace = "AWS/CloudFront", MetricName = "5xxErrorRate", DimensionsMap = new System.Collections.Generic.Dictionary<string, string> { ["DistributionId"] = d.DistributionId, ["Region"] = "Global" }, Region = "us-east-1", Statistic = "Average" }),
        },
    });
}
