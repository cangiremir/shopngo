using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace ShopNGo.BuildingBlocks.Web;

public sealed class CorrelationMiddleware(RequestDelegate next)
{
    public const string HeaderName = "x-correlation-id";

    public async Task Invoke(HttpContext context, ILogger<CorrelationMiddleware> logger)
    {
        var correlationId = context.Request.Headers[HeaderName].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(correlationId))
        {
            correlationId = Guid.NewGuid().ToString("N");
        }

        context.Items[HeaderName] = correlationId;
        context.Response.Headers[HeaderName] = correlationId;
        Activity.Current?.SetTag("correlation.id", correlationId);

        using (logger.BeginScope(new Dictionary<string, object> { ["correlationId"] = correlationId! }))
        {
            await next(context);
        }
    }
}
