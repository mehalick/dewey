using System.Security.Claims;
using System.Text.Json.Serialization;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.AspNetCoreServer.Hosting;
using Amazon.Lambda.Serialization.SystemTextJson;
using Dewey.Shared.Contracts;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateSlimBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, ApiJsonContext.Default);
});

var region = Environment.GetEnvironmentVariable("DEWEY_AWS_REGION") ?? "us-east-1";
var userPoolId = Environment.GetEnvironmentVariable("DEWEY_USER_POOL_ID")
    ?? throw new InvalidOperationException("DEWEY_USER_POOL_ID not set");
var userPoolClientId = Environment.GetEnvironmentVariable("DEWEY_USER_POOL_CLIENT_ID")
    ?? throw new InvalidOperationException("DEWEY_USER_POOL_CLIENT_ID not set");
var authority = $"https://cognito-idp.{region}.amazonaws.com/{userPoolId}";

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = authority;
        options.RequireHttpsMetadata = true;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = authority,
            ValidateAudience = true,
            ValidAudiences = [userPoolClientId],
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            // Cognito ID tokens have `aud` = clientId; access tokens have
            // `client_id` instead. We accept ID tokens here.
        };
    });
builder.Services.AddAuthorization();

builder.Services.AddAWSLambdaHosting(
    LambdaEventSource.HttpApi,
    new SourceGeneratorLambdaJsonSerializer<LambdaJsonContext>());

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/api/health", () => new HealthResponse("ok", "0.1.0"));

app.MapGet("/api/me", (ClaimsPrincipal user) =>
{
    var sub = user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
    var email = user.FindFirstValue("email") ?? string.Empty;
    return Results.Ok(new MeResponse(sub ?? string.Empty, email));
}).RequireAuthorization();

app.Run();

[JsonSerializable(typeof(HealthResponse))]
[JsonSerializable(typeof(MeResponse))]
internal partial class ApiJsonContext : JsonSerializerContext;

[JsonSerializable(typeof(APIGatewayHttpApiV2ProxyRequest))]
[JsonSerializable(typeof(APIGatewayHttpApiV2ProxyResponse))]
internal partial class LambdaJsonContext : JsonSerializerContext;
