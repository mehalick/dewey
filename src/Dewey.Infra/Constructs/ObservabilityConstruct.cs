using Amazon.CDK;
using Amazon.CDK.AWS.CloudWatch;
using Amazon.CDK.AWS.CloudWatch.Actions;
using Amazon.CDK.AWS.Lambda;
using Constructs;

namespace Dewey.Infra.Constructs;

public sealed class ObservabilityConstructProps
{
    public Function ApiFunction { get; init; }
}

public sealed class ObservabilityConstruct : Construct
{
    public ObservabilityConstruct(Construct scope, string id, ObservabilityConstructProps props) : base(scope, id)
    {
        var errorAlarm = new Alarm(this, "ApiErrorRate", new AlarmProps
        {
            Metric = props.ApiFunction.MetricErrors(new Amazon.CDK.AWS.CloudWatch.MetricOptions
            {
                Period = Duration.Minutes(5),
                Statistic = "Sum",
            }),
            Threshold = 5,
            EvaluationPeriods = 1,
            ComparisonOperator = ComparisonOperator.GREATER_THAN_THRESHOLD,
            TreatMissingData = TreatMissingData.NOT_BREACHING,
        });

        var latencyAlarm = new Alarm(this, "ApiP99Latency", new AlarmProps
        {
            Metric = props.ApiFunction.MetricDuration(new Amazon.CDK.AWS.CloudWatch.MetricOptions
            {
                Period = Duration.Minutes(5),
                Statistic = "p99",
            }),
            Threshold = 3000,
            EvaluationPeriods = 2,
            ComparisonOperator = ComparisonOperator.GREATER_THAN_THRESHOLD,
            TreatMissingData = TreatMissingData.NOT_BREACHING,
        });

        new Dashboard(this, "Dashboard", new DashboardProps
        {
            DashboardName = "Dewey",
            Widgets = new[]
            {
                new IWidget[]
                {
                    new GraphWidget(new GraphWidgetProps
                    {
                        Title = "API invocations",
                        Left = new[] { props.ApiFunction.MetricInvocations() },
                    }),
                    new GraphWidget(new GraphWidgetProps
                    {
                        Title = "API errors",
                        Left = new[] { props.ApiFunction.MetricErrors() },
                    }),
                    new GraphWidget(new GraphWidgetProps
                    {
                        Title = "API duration (p50/p99)",
                        Left = new[]
                        {
                            props.ApiFunction.MetricDuration(new Amazon.CDK.AWS.CloudWatch.MetricOptions { Statistic = "p50" }),
                            props.ApiFunction.MetricDuration(new Amazon.CDK.AWS.CloudWatch.MetricOptions { Statistic = "p99" }),
                        },
                    }),
                },
            },
        });

        _ = errorAlarm;
        _ = latencyAlarm;
    }
}
