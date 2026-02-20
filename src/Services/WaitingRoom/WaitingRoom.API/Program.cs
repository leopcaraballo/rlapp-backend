using Serilog;
using WaitingRoom.API.Middleware;
using WaitingRoom.API.Endpoints;
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
using WaitingRoom.Infrastructure.Projections;
using WaitingRoom.Infrastructure.Serialization;
using WaitingRoom.Projections.Abstractions;
using WaitingRoom.Projections.Implementations;
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
services.AddScoped<CallNextCashierCommandHandler>();
services.AddScoped<ValidatePaymentCommandHandler>();
services.AddScoped<MarkPaymentPendingCommandHandler>();
services.AddScoped<MarkAbsentAtCashierCommandHandler>();
services.AddScoped<CancelByPaymentCommandHandler>();
services.AddScoped<ActivateConsultingRoomCommandHandler>();
services.AddScoped<DeactivateConsultingRoomCommandHandler>();
services.AddScoped<ClaimNextPatientCommandHandler>();
services.AddScoped<CallPatientCommandHandler>();
services.AddScoped<CompleteAttentionCommandHandler>();
services.AddScoped<MarkAbsentAtConsultationCommandHandler>();

// Projections (in-memory context for API query runtime)
services.AddSingleton<IWaitingRoomProjectionContext, InMemoryWaitingRoomProjectionContext>();
services.AddSingleton<IProjection>(sp =>
{
    var context = sp.GetRequiredService<IWaitingRoomProjectionContext>();
    var eventStore = sp.GetRequiredService<IEventStore>();
    var logger = sp.GetRequiredService<ILogger<WaitingRoomProjectionEngine>>();
    return new WaitingRoomProjectionEngine(context, eventStore, logger);
});

// ==============================================================================
// API SERVICES
// ==============================================================================

services.AddEndpointsApiExplorer();
services.AddOpenApi();  // Use native .NET 10 OpenAPI instead of Swagger

services.AddCors(options =>
{
    options.AddPolicy("FrontendDev", policy =>
    {
        policy
            .WithOrigins("http://localhost:3000", "http://127.0.0.1:3000")
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

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
app.UseCors("FrontendDev");

// Development tools
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();  // Serve OpenAPI schema at /openapi/v1.json
    app.UseHttpsRedirection();
}

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

var queryGroup = app.MapGroup("/api/v1/waiting-room")
    .WithTags("Waiting Room Queries");
WaitingRoomQueryEndpoints.MapEndpoints(queryGroup);

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
    IProjection projection,
    IEventStore eventStore,
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
        Age = dto.Age,
        IsPregnant = dto.IsPregnant,
        Notes = dto.Notes,
        Actor = dto.Actor,
        CorrelationId = correlationId
    };

    // Execute command via handler
    var eventCount = await handler.HandleAsync(command, cancellationToken);
    var queueEvents = await eventStore.GetEventsAsync(dto.QueueId, cancellationToken);
    await projection.ProcessEventsAsync(queueEvents, cancellationToken);

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

app.MapPost("/api/reception/register", async (
    CheckInPatientDto dto,
    HttpContext httpContext,
    CheckInPatientCommandHandler handler,
    IProjection projection,
    IEventStore eventStore,
    CancellationToken cancellationToken) =>
{
    var correlationId = httpContext.Items["CorrelationId"]?.ToString() ?? Guid.NewGuid().ToString();

    var command = new CheckInPatientCommand
    {
        QueueId = dto.QueueId,
        PatientId = dto.PatientId,
        PatientName = dto.PatientName,
        Priority = dto.Priority,
        ConsultationType = dto.ConsultationType,
        Age = dto.Age,
        IsPregnant = dto.IsPregnant,
        Notes = dto.Notes,
        Actor = dto.Actor,
        CorrelationId = correlationId
    };

    var eventCount = await handler.HandleAsync(command, cancellationToken);
    var queueEvents = await eventStore.GetEventsAsync(dto.QueueId, cancellationToken);
    await projection.ProcessEventsAsync(queueEvents, cancellationToken);

    return Results.Ok(new
    {
        Success = true,
        Message = "Patient registered successfully",
        CorrelationId = correlationId,
        EventCount = eventCount
    });
})
.WithName("RegisterPatientReception")
.WithTags("Reception")
.Produces(200)
.Produces(400)
.Produces(404)
.Produces(409)
.Produces(500);

app.MapPost("/api/cashier/call-next", async (
    CallNextCashierDto dto,
    HttpContext httpContext,
    CallNextCashierCommandHandler handler,
    IProjection projection,
    IEventStore eventStore,
    CancellationToken cancellationToken) =>
{
    var correlationId = httpContext.Items["CorrelationId"]?.ToString() ?? Guid.NewGuid().ToString();

    var command = new CallNextCashierCommand
    {
        QueueId = dto.QueueId,
        Actor = dto.Actor,
        CashierDeskId = dto.CashierDeskId,
        CorrelationId = correlationId
    };

    var result = await handler.HandleAsync(command, cancellationToken);
    var queueEvents = await eventStore.GetEventsAsync(dto.QueueId, cancellationToken);
    await projection.ProcessEventsAsync(queueEvents, cancellationToken);

    return Results.Ok(new
    {
        Success = true,
        Message = "Patient called at cashier successfully",
        CorrelationId = correlationId,
        EventCount = result.EventCount,
        PatientId = result.PatientId
    });
})
.WithName("CallNextCashier")
.WithTags("Cashier")
.Produces(200)
.Produces(400)
.Produces(404)
.Produces(409)
.Produces(500);

app.MapPost("/api/cashier/validate-payment", async (
    ValidatePaymentDto dto,
    HttpContext httpContext,
    ValidatePaymentCommandHandler handler,
    IProjection projection,
    IEventStore eventStore,
    CancellationToken cancellationToken) =>
{
    var correlationId = httpContext.Items["CorrelationId"]?.ToString() ?? Guid.NewGuid().ToString();

    var command = new ValidatePaymentCommand
    {
        QueueId = dto.QueueId,
        PatientId = dto.PatientId,
        Actor = dto.Actor,
        PaymentReference = dto.PaymentReference,
        CorrelationId = correlationId
    };

    var eventCount = await handler.HandleAsync(command, cancellationToken);
    var queueEvents = await eventStore.GetEventsAsync(dto.QueueId, cancellationToken);
    await projection.ProcessEventsAsync(queueEvents, cancellationToken);

    return Results.Ok(new
    {
        Success = true,
        Message = "Payment validated successfully",
        CorrelationId = correlationId,
        EventCount = eventCount,
        PatientId = dto.PatientId
    });
})
.WithName("ValidatePayment")
.WithTags("Cashier")
.Produces(200)
.Produces(400)
.Produces(404)
.Produces(409)
.Produces(500);

app.MapPost("/api/cashier/mark-payment-pending", async (
    MarkPaymentPendingDto dto,
    HttpContext httpContext,
    MarkPaymentPendingCommandHandler handler,
    IProjection projection,
    IEventStore eventStore,
    CancellationToken cancellationToken) =>
{
    var correlationId = httpContext.Items["CorrelationId"]?.ToString() ?? Guid.NewGuid().ToString();

    var command = new MarkPaymentPendingCommand
    {
        QueueId = dto.QueueId,
        PatientId = dto.PatientId,
        Actor = dto.Actor,
        Reason = dto.Reason,
        CorrelationId = correlationId
    };

    var eventCount = await handler.HandleAsync(command, cancellationToken);
    var queueEvents = await eventStore.GetEventsAsync(dto.QueueId, cancellationToken);
    await projection.ProcessEventsAsync(queueEvents, cancellationToken);

    return Results.Ok(new
    {
        Success = true,
        Message = "Payment marked as pending",
        CorrelationId = correlationId,
        EventCount = eventCount,
        PatientId = dto.PatientId
    });
})
.WithName("MarkPaymentPending")
.WithTags("Cashier")
.Produces(200)
.Produces(400)
.Produces(404)
.Produces(409)
.Produces(500);

app.MapPost("/api/cashier/mark-absent", async (
    MarkAbsentAtCashierDto dto,
    HttpContext httpContext,
    MarkAbsentAtCashierCommandHandler handler,
    IProjection projection,
    IEventStore eventStore,
    CancellationToken cancellationToken) =>
{
    var correlationId = httpContext.Items["CorrelationId"]?.ToString() ?? Guid.NewGuid().ToString();

    var command = new MarkAbsentAtCashierCommand
    {
        QueueId = dto.QueueId,
        PatientId = dto.PatientId,
        Actor = dto.Actor,
        CorrelationId = correlationId
    };

    var eventCount = await handler.HandleAsync(command, cancellationToken);
    var queueEvents = await eventStore.GetEventsAsync(dto.QueueId, cancellationToken);
    await projection.ProcessEventsAsync(queueEvents, cancellationToken);

    return Results.Ok(new
    {
        Success = true,
        Message = "Patient marked absent at cashier",
        CorrelationId = correlationId,
        EventCount = eventCount,
        PatientId = dto.PatientId
    });
})
.WithName("MarkAbsentAtCashier")
.WithTags("Cashier")
.Produces(200)
.Produces(400)
.Produces(404)
.Produces(409)
.Produces(500);

app.MapPost("/api/cashier/cancel-payment", async (
    CancelByPaymentDto dto,
    HttpContext httpContext,
    CancelByPaymentCommandHandler handler,
    IProjection projection,
    IEventStore eventStore,
    CancellationToken cancellationToken) =>
{
    var correlationId = httpContext.Items["CorrelationId"]?.ToString() ?? Guid.NewGuid().ToString();

    var command = new CancelByPaymentCommand
    {
        QueueId = dto.QueueId,
        PatientId = dto.PatientId,
        Actor = dto.Actor,
        Reason = dto.Reason,
        CorrelationId = correlationId
    };

    var eventCount = await handler.HandleAsync(command, cancellationToken);
    var queueEvents = await eventStore.GetEventsAsync(dto.QueueId, cancellationToken);
    await projection.ProcessEventsAsync(queueEvents, cancellationToken);

    return Results.Ok(new
    {
        Success = true,
        Message = "Patient cancelled by payment policy",
        CorrelationId = correlationId,
        EventCount = eventCount,
        PatientId = dto.PatientId
    });
})
.WithName("CancelByPayment")
.WithTags("Cashier")
.Produces(200)
.Produces(400)
.Produces(404)
.Produces(409)
.Produces(500);

app.MapPost("/api/medical/call-next", async (
    ClaimNextPatientDto dto,
    HttpContext httpContext,
    ClaimNextPatientCommandHandler handler,
    IProjection projection,
    IEventStore eventStore,
    CancellationToken cancellationToken) =>
{
    var correlationId = httpContext.Items["CorrelationId"]?.ToString() ?? Guid.NewGuid().ToString();

    var command = new ClaimNextPatientCommand
    {
        QueueId = dto.QueueId,
        Actor = dto.Actor,
        CorrelationId = correlationId,
        StationId = dto.StationId
    };

    var result = await handler.HandleAsync(command, cancellationToken);
    var queueEvents = await eventStore.GetEventsAsync(dto.QueueId, cancellationToken);
    await projection.ProcessEventsAsync(queueEvents, cancellationToken);

    return Results.Ok(new
    {
        Success = true,
        Message = "Next patient called for medical attention",
        CorrelationId = correlationId,
        EventCount = result.EventCount,
        PatientId = result.PatientId
    });
})
.WithName("MedicalCallNext")
.WithTags("Medical")
.Produces(200)
.Produces(400)
.Produces(404)
.Produces(409)
.Produces(500);

app.MapPost("/api/medical/consulting-room/activate", async (
    ActivateConsultingRoomDto dto,
    HttpContext httpContext,
    ActivateConsultingRoomCommandHandler handler,
    IProjection projection,
    IEventStore eventStore,
    CancellationToken cancellationToken) =>
{
    var correlationId = httpContext.Items["CorrelationId"]?.ToString() ?? Guid.NewGuid().ToString();

    var command = new ActivateConsultingRoomCommand
    {
        QueueId = dto.QueueId,
        ConsultingRoomId = dto.ConsultingRoomId,
        Actor = dto.Actor,
        CorrelationId = correlationId
    };

    var eventCount = await handler.HandleAsync(command, cancellationToken);
    var queueEvents = await eventStore.GetEventsAsync(dto.QueueId, cancellationToken);
    await projection.ProcessEventsAsync(queueEvents, cancellationToken);

    return Results.Ok(new
    {
        Success = true,
        Message = "Consulting room activated",
        CorrelationId = correlationId,
        EventCount = eventCount,
        ConsultingRoomId = dto.ConsultingRoomId
    });
})
.WithName("ActivateConsultingRoom")
.WithTags("Medical")
.Produces(200)
.Produces(400)
.Produces(404)
.Produces(409)
.Produces(500);

app.MapPost("/api/medical/consulting-room/deactivate", async (
    DeactivateConsultingRoomDto dto,
    HttpContext httpContext,
    DeactivateConsultingRoomCommandHandler handler,
    IProjection projection,
    IEventStore eventStore,
    CancellationToken cancellationToken) =>
{
    var correlationId = httpContext.Items["CorrelationId"]?.ToString() ?? Guid.NewGuid().ToString();

    var command = new DeactivateConsultingRoomCommand
    {
        QueueId = dto.QueueId,
        ConsultingRoomId = dto.ConsultingRoomId,
        Actor = dto.Actor,
        CorrelationId = correlationId
    };

    var eventCount = await handler.HandleAsync(command, cancellationToken);
    var queueEvents = await eventStore.GetEventsAsync(dto.QueueId, cancellationToken);
    await projection.ProcessEventsAsync(queueEvents, cancellationToken);

    return Results.Ok(new
    {
        Success = true,
        Message = "Consulting room deactivated",
        CorrelationId = correlationId,
        EventCount = eventCount,
        ConsultingRoomId = dto.ConsultingRoomId
    });
})
.WithName("DeactivateConsultingRoom")
.WithTags("Medical")
.Produces(200)
.Produces(400)
.Produces(404)
.Produces(409)
.Produces(500);

app.MapPost("/api/medical/start-consultation", async (
    CallPatientDto dto,
    HttpContext httpContext,
    CallPatientCommandHandler handler,
    IProjection projection,
    IEventStore eventStore,
    CancellationToken cancellationToken) =>
{
    var correlationId = httpContext.Items["CorrelationId"]?.ToString() ?? Guid.NewGuid().ToString();

    var command = new CallPatientCommand
    {
        QueueId = dto.QueueId,
        PatientId = dto.PatientId,
        Actor = dto.Actor,
        CorrelationId = correlationId
    };

    var eventCount = await handler.HandleAsync(command, cancellationToken);
    var queueEvents = await eventStore.GetEventsAsync(dto.QueueId, cancellationToken);
    await projection.ProcessEventsAsync(queueEvents, cancellationToken);

    return Results.Ok(new
    {
        Success = true,
        Message = "Consultation started successfully",
        CorrelationId = correlationId,
        EventCount = eventCount,
        PatientId = dto.PatientId
    });
})
.WithName("StartConsultation")
.WithTags("Medical")
.Produces(200)
.Produces(400)
.Produces(404)
.Produces(409)
.Produces(500);

app.MapPost("/api/medical/finish-consultation", async (
    CompleteAttentionDto dto,
    HttpContext httpContext,
    CompleteAttentionCommandHandler handler,
    IProjection projection,
    IEventStore eventStore,
    CancellationToken cancellationToken) =>
{
    var correlationId = httpContext.Items["CorrelationId"]?.ToString() ?? Guid.NewGuid().ToString();

    var command = new CompleteAttentionCommand
    {
        QueueId = dto.QueueId,
        PatientId = dto.PatientId,
        Actor = dto.Actor,
        Outcome = dto.Outcome,
        Notes = dto.Notes,
        CorrelationId = correlationId
    };

    var eventCount = await handler.HandleAsync(command, cancellationToken);
    var queueEvents = await eventStore.GetEventsAsync(dto.QueueId, cancellationToken);
    await projection.ProcessEventsAsync(queueEvents, cancellationToken);

    return Results.Ok(new
    {
        Success = true,
        Message = "Consultation finished successfully",
        CorrelationId = correlationId,
        EventCount = eventCount,
        PatientId = dto.PatientId
    });
})
.WithName("FinishConsultation")
.WithTags("Medical")
.Produces(200)
.Produces(400)
.Produces(404)
.Produces(409)
.Produces(500);

app.MapPost("/api/medical/mark-absent", async (
    MarkAbsentAtConsultationDto dto,
    HttpContext httpContext,
    MarkAbsentAtConsultationCommandHandler handler,
    IProjection projection,
    IEventStore eventStore,
    CancellationToken cancellationToken) =>
{
    var correlationId = httpContext.Items["CorrelationId"]?.ToString() ?? Guid.NewGuid().ToString();

    var command = new MarkAbsentAtConsultationCommand
    {
        QueueId = dto.QueueId,
        PatientId = dto.PatientId,
        Actor = dto.Actor,
        CorrelationId = correlationId
    };

    var eventCount = await handler.HandleAsync(command, cancellationToken);
    var queueEvents = await eventStore.GetEventsAsync(dto.QueueId, cancellationToken);
    await projection.ProcessEventsAsync(queueEvents, cancellationToken);

    return Results.Ok(new
    {
        Success = true,
        Message = "Patient marked absent at consultation",
        CorrelationId = correlationId,
        EventCount = eventCount,
        PatientId = dto.PatientId
    });
})
.WithName("MarkAbsentAtConsultation")
.WithTags("Medical")
.Produces(200)
.Produces(400)
.Produces(404)
.Produces(409)
.Produces(500);

app.MapPost("/api/waiting-room/claim-next", async (
    ClaimNextPatientDto dto,
    HttpContext httpContext,
    ClaimNextPatientCommandHandler handler,
    IProjection projection,
    IEventStore eventStore,
    ILogger<Program> logger,
    CancellationToken cancellationToken) =>
{
    var correlationId = httpContext.Items["CorrelationId"]?.ToString() ?? Guid.NewGuid().ToString();

    logger.LogInformation(
        "ClaimNext request received. CorrelationId: {CorrelationId}, QueueId: {QueueId}",
        correlationId,
        dto.QueueId);

    var command = new ClaimNextPatientCommand
    {
        QueueId = dto.QueueId,
        Actor = dto.Actor,
        CorrelationId = correlationId,
        StationId = dto.StationId
    };

    var result = await handler.HandleAsync(command, cancellationToken);
    var queueEvents = await eventStore.GetEventsAsync(dto.QueueId, cancellationToken);
    await projection.ProcessEventsAsync(queueEvents, cancellationToken);

    return Results.Ok(new
    {
        Success = true,
        Message = "Patient claimed successfully",
        CorrelationId = correlationId,
        EventCount = result.EventCount,
        PatientId = result.PatientId
    });
})
.WithName("ClaimNextPatient")
.WithTags("WaitingRoom")
.Produces(200)
.Produces(400)
.Produces(404)
.Produces(409)
.Produces(500);

app.MapPost("/api/waiting-room/call-patient", async (
    CallPatientDto dto,
    HttpContext httpContext,
    CallPatientCommandHandler handler,
    IProjection projection,
    IEventStore eventStore,
    CancellationToken cancellationToken) =>
{
    var correlationId = httpContext.Items["CorrelationId"]?.ToString() ?? Guid.NewGuid().ToString();

    var command = new CallPatientCommand
    {
        QueueId = dto.QueueId,
        PatientId = dto.PatientId,
        Actor = dto.Actor,
        CorrelationId = correlationId
    };

    var eventCount = await handler.HandleAsync(command, cancellationToken);
    var queueEvents = await eventStore.GetEventsAsync(dto.QueueId, cancellationToken);
    await projection.ProcessEventsAsync(queueEvents, cancellationToken);

    return Results.Ok(new
    {
        Success = true,
        Message = "Patient called successfully",
        CorrelationId = correlationId,
        EventCount = eventCount,
        PatientId = dto.PatientId
    });
})
.WithName("CallPatient")
.WithTags("WaitingRoom")
.Produces(200)
.Produces(400)
.Produces(404)
.Produces(409)
.Produces(500);

app.MapPost("/api/waiting-room/complete-attention", async (
    CompleteAttentionDto dto,
    HttpContext httpContext,
    CompleteAttentionCommandHandler handler,
    IProjection projection,
    IEventStore eventStore,
    CancellationToken cancellationToken) =>
{
    var correlationId = httpContext.Items["CorrelationId"]?.ToString() ?? Guid.NewGuid().ToString();

    var command = new CompleteAttentionCommand
    {
        QueueId = dto.QueueId,
        PatientId = dto.PatientId,
        Actor = dto.Actor,
        Outcome = dto.Outcome,
        Notes = dto.Notes,
        CorrelationId = correlationId
    };

    var eventCount = await handler.HandleAsync(command, cancellationToken);
    var queueEvents = await eventStore.GetEventsAsync(dto.QueueId, cancellationToken);
    await projection.ProcessEventsAsync(queueEvents, cancellationToken);

    return Results.Ok(new
    {
        Success = true,
        Message = "Attention completed successfully",
        CorrelationId = correlationId,
        EventCount = eventCount,
        PatientId = dto.PatientId
    });
})
.WithName("CompleteAttention")
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
