using Serilog;
using WaitingRoom.API.Middleware;
using WaitingRoom.Application.CommandHandlers;
using WaitingRoom.Application.DTOs;
using WaitingRoom.Application.Commands;
using WaitingRoom.Application.Ports;
using WaitingRoom.Application.Services;
using WaitingRoom.Domain.Events;
using WaitingRoom.Infrastructure.Messaging;
using WaitingRoom.Infrastructure.Persistence.EventStore;
using WaitingRoom.Infrastructure.Persistence.Outbox;
using WaitingRoom.Infrastructure.Observability;
using WaitingRoom.Infrastructure.Serialization;
using BuildingBlocks.EventSourcing;
using BuildingBlocks.Observability;

// ==============================================================================
// RLAPP — WaitingRoom.API
// Hexagonal Architecture — Presentation Layer (Adapter)
//
// Responsibilities:
// - Expose HTTP endpoints
// - Handle authentication/authorization
// - Map DTOs to Commands
// - Inject CorrelationId
// - Route to Application layer
//
// Architecture:
// - NO business logic
// - NO domain knowledge
// - Pure adapter/presenter
// - Dependency Injection composition root
// ==============================================================================

var builder = WebApplication.CreateBuilder(args);

// ==============================================================================
// LOGGING CONFIGURATION — Structured Logging with Serilog
// ==============================================================================

Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .CreateLogger();

builder.Host.UseSerilog();

// ==============================================================================
// CONFIGURATION OPTIONS
// ==============================================================================

var connectionString = builder.Configuration.GetConnectionString("EventStore")
    ?? throw new InvalidOperationException("EventStore connection string is required");

var rabbitMqOptions = new RabbitMqOptions();
builder.Configuration.GetSection("RabbitMq").Bind(rabbitMqOptions);

// ==============================================================================
// DEPENDENCY INJECTION — Composition Root (Hexagonal Architecture)
// ==============================================================================

var services = builder.Services;

// Infrastructure — Outbox Store
services.AddSingleton<IOutboxStore>(sp => new PostgresOutboxStore(connectionString));

// Infrastructure — Lag Tracker
services.AddSingleton<IEventLagTracker>(sp => new PostgresEventLagTracker(connectionString));

// Infrastructure — Event Type Registry
services.AddSingleton<EventTypeRegistry>(sp => EventTypeRegistry.CreateDefault());

// Infrastructure — Event Serializer
services.AddSingleton<EventSerializer>();

// Infrastructure — Event Publisher (Outbox only; Worker dispatches)
services.AddSingleton<IEventPublisher, OutboxEventPublisher>();

// Infrastructure — Event Store (PostgreSQL)
services.AddSingleton<IEventStore>(sp =>
{
    var serializer = sp.GetRequiredService<EventSerializer>();
    var outboxStore = sp.GetRequiredService<IOutboxStore>();
    var lagTracker = sp.GetRequiredService<IEventLagTracker>();
    return new PostgresEventStore(connectionString, serializer, outboxStore, lagTracker);
});

// Application — Clock
services.AddSingleton<IClock, SystemClock>();

// Application — Command Handlers
services.AddScoped<CheckInPatientCommandHandler>();

// ==============================================================================
// API SERVICES
// ==============================================================================

services.AddEndpointsApiExplorer();
services.AddOpenApi();  // Use native .NET 10 OpenAPI instead of Swagger

// Health Checks
services.AddHealthChecks()
    .AddCheck("self", () => Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy())
    .AddNpgSql(connectionString, name: "postgres", tags: new[] { "db", "postgres" });

// ==============================================================================
// APPLICATION PIPELINE
// ==============================================================================

var app = builder.Build();

// Middleware Pipeline (order matters)
app.UseCorrelationId();
app.UseMiddleware<ExceptionHandlerMiddleware>();

// Development tools
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();  // Serve OpenAPI schema at /openapi/v1.json
}

app.UseHttpsRedirection();

// ==============================================================================
// HEALTH CHECKS ENDPOINTS
// ==============================================================================

app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("self") || check.Tags.Count == 0
});

app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = _ => true // All checks
});

// ==============================================================================
// API ENDPOINTS — Minimal API Pattern
// ==============================================================================

/// <summary>
/// POST /api/waiting-room/check-in
///
/// Check in a patient to a waiting queue.
///
/// Architecture:
/// - Entry point to the system (Hexagonal Adapter)
/// - Maps DTO → Command
/// - Injects CorrelationId
/// - Delegates to Application layer (CheckInPatientCommandHandler)
/// - Returns HTTP response
///
/// Flow:
/// HTTP Request → DTO → Command → Handler → Aggregate → Events → EventStore → Response
/// </summary>
app.MapPost("/api/waiting-room/check-in", async (
    CheckInPatientDto dto,
    HttpContext httpContext,
    CheckInPatientCommandHandler handler,
    ILogger<Program> logger,
    CancellationToken cancellationToken) =>
{
    var correlationId = httpContext.Items["CorrelationId"]?.ToString() ?? Guid.NewGuid().ToString();

    logger.LogInformation(
        "CheckIn request received. CorrelationId: {CorrelationId}, QueueId: {QueueId}, PatientId: {PatientId}",
        correlationId,
        dto.QueueId,
        dto.PatientId);

    // Map DTO → Command
    var command = new CheckInPatientCommand
    {
        QueueId = dto.QueueId,
        PatientId = dto.PatientId,
        PatientName = dto.PatientName,
        Priority = dto.Priority,
        ConsultationType = dto.ConsultationType,
        Notes = dto.Notes,
        Actor = dto.Actor,
        CorrelationId = correlationId
    };

    // Execute command via handler
    var eventCount = await handler.HandleAsync(command, cancellationToken);

    logger.LogInformation(
        "CheckIn completed. CorrelationId: {CorrelationId}, EventCount: {EventCount}",
        correlationId,
        eventCount);

    return Results.Ok(new
    {
        Success = true,
        Message = "Patient checked in successfully",
        CorrelationId = correlationId,
        EventCount = eventCount
    });
})
.WithName("CheckInPatient")
.WithTags("WaitingRoom")
.Produces(200)
.Produces(400)
.Produces(404)
.Produces(409)
.Produces(500);

// ==============================================================================
// STARTUP
// ==============================================================================

// Ensure database schema exists (for development)
if (app.Environment.IsDevelopment())
{
    var eventStore = app.Services.GetRequiredService<IEventStore>();
    if (eventStore is PostgresEventStore postgresEventStore)
    {
        await postgresEventStore.EnsureSchemaAsync();
        Log.Information("Database schema initialized");
    }
}

Log.Information("Starting WaitingRoom.API...");
Log.Information("Environment: {Environment}", app.Environment.EnvironmentName);

app.Run();

Log.Information("WaitingRoom.API stopped");
Log.CloseAndFlush();
