using System.Diagnostics;
using System.Security.Claims;

namespace Dewey.Api;

public static class RequestLogging
{
    // Emits one structured log line per request. With LoggingFormat.JSON on
    // the Lambda, ILogger output becomes a JSON record in CloudWatch and these
    // properties end up as searchable fields. The X-Ray trace id is set by
    // the Lambda runtime in the `_X_AMZN_TRACE_ID` env var when active tracing
    // is enabled; we forward it as a correlation field.
    public static IApplicationBuilder UseRequestLogging(this IApplicationBuilder app)
    {
        var logger = app.ApplicationServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("Dewey.Api.Request");

        return app.Use(async (ctx, next) =>
        {
            var sw = Stopwatch.StartNew();
            var traceId = Environment.GetEnvironmentVariable("_X_AMZN_TRACE_ID") ?? "";
            try
            {
                await next();
            }
            finally
            {
                sw.Stop();
                var sub = ctx.User.FindFirstValue("sub") ?? "-";
                using (logger.BeginScope(new Dictionary<string, object?>
                {
                    ["method"] = ctx.Request.Method,
                    ["path"] = ctx.Request.Path.Value,
                    ["status"] = ctx.Response.StatusCode,
                    ["durationMs"] = sw.ElapsedMilliseconds,
                    ["sub"] = sub,
                    ["traceId"] = traceId,
                }))
                {
                    logger.LogInformation(
                        "{Method} {Path} -> {Status} in {DurationMs}ms",
                        ctx.Request.Method, ctx.Request.Path, ctx.Response.StatusCode, sw.ElapsedMilliseconds);
                }
            }
        });
    }
}
