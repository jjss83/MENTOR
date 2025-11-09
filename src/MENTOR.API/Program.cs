using MENTOR.Core.Interfaces;
using MENTOR.Core.Models;
using MENTOR.Core.Services;
using MENTOR.Infrastructure.Database;
using MENTOR.Infrastructure.MLAgents;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .CreateLogger();

builder.Host.UseSerilog();

// Configure options
builder.Services.Configure<MLAgentsOptions>(
    builder.Configuration.GetSection(MLAgentsOptions.SectionName));
builder.Services.Configure<DatabaseOptions>(
    builder.Configuration.GetSection(DatabaseOptions.SectionName));

// Register services
builder.Services.AddSingleton<ITrainingRepository>(sp =>
{
    var connectionString = builder.Configuration
        .GetSection(DatabaseOptions.SectionName)
        .Get<DatabaseOptions>()?.ConnectionString ?? "Filename=mentor.db";
    return new LiteDbTrainingRepository(connectionString);
});

builder.Services.AddSingleton<ITrainingProcessManager, MLAgentsProcessManager>();
builder.Services.AddSingleton<ITrainingService, TrainingService>();

// Add CORS for development
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();

app.UseSerilogRequestLogging();
app.UseCors();

// Health check endpoint
app.MapGet("/health", () => Results.Ok(new
{
    status = "healthy",
    timestamp = DateTime.UtcNow
}))
.WithName("HealthCheck")
.WithTags("Health");

// Training endpoints
app.MapPost("/api/training/start", async (
    TrainingRequest request,
    ITrainingService service) =>
{
    var result = await service.StartTrainingAsync(request);

    return result.IsSuccess
        ? Results.Ok(result.Value)
        : Results.BadRequest(new { error = result.Error });
})
.WithName("StartTraining")
.WithTags("Training");

app.MapGet("/api/training/{id:guid}", async (
    Guid id,
    ITrainingService service) =>
{
    var session = await service.GetSessionAsync(id);
    return session != null
        ? Results.Ok(session)
        : Results.NotFound(new { error = "Training session not found" });
})
.WithName("GetTrainingSession")
.WithTags("Training");

app.MapGet("/api/training/run/{runId}", async (
    string runId,
    ITrainingService service) =>
{
    var session = await service.GetSessionByRunIdAsync(runId);
    return session != null
        ? Results.Ok(session)
        : Results.NotFound(new { error = "Training session not found" });
})
.WithName("GetTrainingSessionByRunId")
.WithTags("Training");

app.MapPost("/api/training/{id:guid}/stop", async (
    Guid id,
    ITrainingService service) =>
{
    var result = await service.StopTrainingAsync(id);
    return result.IsSuccess
        ? Results.Ok(new { message = "Training stopped successfully" })
        : Results.BadRequest(new { error = result.Error });
})
.WithName("StopTraining")
.WithTags("Training");

app.MapGet("/api/training", async (ITrainingService service) =>
{
    var sessions = await service.GetAllSessionsAsync();
    return Results.Ok(sessions);
})
.WithName("GetAllTrainingSessions")
.WithTags("Training");

app.MapGet("/api/training/active", async (ITrainingService service) =>
{
    var sessions = await service.GetActiveSessionsAsync();
    return Results.Ok(sessions);
})
.WithName("GetActiveSessions")
.WithTags("Training");

Log.Information("MENTOR API starting...");
app.Run();
Log.Information("MENTOR API stopped");
Log.CloseAndFlush();
