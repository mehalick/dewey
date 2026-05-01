using Amazon.CDK;

namespace Dewey.Infra;

internal static class Program
{
    public static void Main(string[] args)
    {
        var app = new App();

        new DeweyStack(app, "DeweyStack", new StackProps
        {
            Env = new Amazon.CDK.Environment
            {
                Account = System.Environment.GetEnvironmentVariable("CDK_DEFAULT_ACCOUNT"),
                Region = System.Environment.GetEnvironmentVariable("CDK_DEFAULT_REGION"),
            },
            Description = "Dewey - book reading tracker (M1 skeleton)",
        });

        app.Synth();
    }
}
