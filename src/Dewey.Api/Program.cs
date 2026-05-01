using System.Text.Json.Serialization;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.AspNetCoreServer.Hosting;
using Amazon.Lambda.Serialization.SystemTextJson;
using Dewey.Shared.Contracts;

var builder = WebApplication.CreateSlimBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, ApiJsonContext.Default);
});

builder.Services.AddAWSLambdaHosting(
    LambdaEventSource.HttpApi,
    new SourceGeneratorLambdaJsonSerializer<LambdaJsonContext>());

var app = builder.Build();

app.MapGet("/api/health", () => new HealthResponse("ok", "0.1.0"));

app.Run();

[JsonSerializable(typeof(HealthResponse))]
internal partial class ApiJsonContext : JsonSerializerContext;

[JsonSerializable(typeof(APIGatewayHttpApiV2ProxyRequest))]
[JsonSerializable(typeof(APIGatewayHttpApiV2ProxyResponse))]
internal partial class LambdaJsonContext : JsonSerializerContext;
