using System.Diagnostics;
using Microsoft.AspNetCore.Http;

namespace ShopNGo.BuildingBlocks.Web;

public static class HttpContextExtensions
{
    public static string GetCorrelationId(this HttpContext context)
        => context.Items[CorrelationMiddleware.HeaderName] as string ?? context.TraceIdentifier;

    public static string? GetTraceParent(this HttpContext context) => Activity.Current?.Id;
}
