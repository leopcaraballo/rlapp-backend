namespace WaitingRoom.API.Middleware;

/// <summary>
/// Middleware to inject CorrelationId into request context.
/// Ensures every request is traceable across distributed system.
///
/// Flow:
/// - If CorrelationId header exists → use it (client-provided)
/// - If not → generate new GUID
/// - Inject into logging context
/// - Add to response headers for traceability
///
/// Architecture:
/// - Infrastructure concern (not domain)
/// - Enables distributed tracing
/// - Observable by design
/// </summary>
public sealed class CorrelationIdMiddleware
{
    private const string CorrelationIdHeaderName = "X-Correlation-Id";
    private readonly RequestDelegate _next;

    public CorrelationIdMiddleware(RequestDelegate next)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Extract or generate CorrelationId
        var correlationId = context.Request.Headers[CorrelationIdHeaderName].FirstOrDefault()
            ?? Guid.NewGuid().ToString();

        // Add to HttpContext for access in controllers/handlers
        context.Items["CorrelationId"] = correlationId;

        // Add to response headers for client traceability
        context.Response.Headers[CorrelationIdHeaderName] = correlationId;

        // Propagate to next middleware
        await _next(context);
    }
}

/// <summary>
/// Extension method for registering CorrelationIdMiddleware.
/// </summary>
public static class CorrelationIdMiddlewareExtensions
{
    public static IApplicationBuilder UseCorrelationId(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<CorrelationIdMiddleware>();
    }
}
