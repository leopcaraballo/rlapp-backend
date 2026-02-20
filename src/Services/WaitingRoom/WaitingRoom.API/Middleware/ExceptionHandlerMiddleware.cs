namespace WaitingRoom.API.Middleware;

using System.Net;
using System.Text.Json;
using WaitingRoom.Application.Exceptions;
using WaitingRoom.Domain.Exceptions;

/// <summary>
/// Global exception handler middleware.
/// Converts exceptions to appropriate HTTP responses.
///
/// Responsibilities:
/// - Catch unhandled exceptions
/// - Map domain/application exceptions to HTTP status codes
/// - Return standardized error responses
/// - Log errors with correlation tracking
///
/// Architecture:
/// - Infrastructure concern
/// - Protects API boundary
/// - Does NOT contain business logic
/// </summary>
public sealed class ExceptionHandlerMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlerMiddleware> _logger;

    public ExceptionHandlerMiddleware(
        RequestDelegate next,
        ILogger<ExceptionHandlerMiddleware> logger)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var correlationId = context.Items["CorrelationId"]?.ToString() ?? "unknown";

        var (statusCode, errorResponse) = exception switch
        {
            DomainException domainEx => (
                HttpStatusCode.BadRequest,
                new ErrorResponse
                {
                    Error = "DomainViolation",
                    Message = domainEx.Message,
                    CorrelationId = correlationId
                }),

            AggregateNotFoundException notFoundEx => (
                HttpStatusCode.NotFound,
                new ErrorResponse
                {
                    Error = "AggregateNotFound",
                    Message = notFoundEx.Message,
                    CorrelationId = correlationId
                }),

            EventConflictException conflictEx => (
                HttpStatusCode.Conflict,
                new ErrorResponse
                {
                    Error = "ConcurrencyConflict",
                    Message = "The resource was modified by another request. Please retry.",
                    CorrelationId = correlationId
                }),

            _ => (
                HttpStatusCode.InternalServerError,
                new ErrorResponse
                {
                    Error = "InternalServerError",
                    Message = "An unexpected error occurred. Please contact support.",
                    CorrelationId = correlationId
                })
        };

        // Log error with correlation
        _logger.LogError(
            exception,
            "Error handling request. CorrelationId: {CorrelationId}, StatusCode: {StatusCode}",
            correlationId,
            statusCode);

        // Write response
        context.Response.ContentType = "application/json";
        context.Response.StatusCode = (int)statusCode;

        var json = JsonSerializer.Serialize(errorResponse, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        await context.Response.WriteAsync(json);
    }
}

/// <summary>
/// Standardized error response model.
/// </summary>
public sealed record ErrorResponse
{
    public required string Error { get; init; }
    public required string Message { get; init; }
    public required string CorrelationId { get; init; }
}

/// <summary>
/// Extension method for registering ExceptionHandlerMiddleware.
/// </summary>
public static class GlobalExceptionHandlerMiddlewareExtensions
{
    public static IApplicationBuilder UseGlobalExceptionHandler(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<ExceptionHandlerMiddleware>();
    }
}
