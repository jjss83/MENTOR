using MentorMlApi.Models;
using MentorMlApi.Options;
using MentorMlApi.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new()
    {
        Title = "Mentor ML API",
        Version = "v1",
        Description = "API for running ML-Agents training in a configured Conda environment"
    });
});

builder.Services.Configure<MlAgentsSettings>(builder.Configuration.GetSection("MlAgents"));
builder.Services.AddSingleton<IMlAgentsProcessTracker, MlAgentsProcessTracker>();
builder.Services.AddSingleton<IMlAgentsRunner, MlAgentsRunner>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "Mentor ML API v1");
    });
}

app.UseHttpsRedirection();

app.MapPost("/mlagents/run", async Task<IResult> (MlAgentsRunRequest? request, IMlAgentsRunner runner, CancellationToken cancellationToken) =>
    {
        var payload = request ?? new MlAgentsRunRequest();
        var result = await runner.RunTrainingAsync(payload, cancellationToken).ConfigureAwait(false);
        return Results.Ok(result);
    })
    .WithName("RunMlAgentsTraining")
    .WithDescription("Runs the configured ML-Agents training command inside the configured Conda environment.")
    .WithOpenApi();

app.MapGet("/mlagents/processes", (IMlAgentsProcessTracker tracker)
        => Results.Ok(tracker.GetRunningProcesses()))
    .WithName("GetRunningMlAgentsProcesses")
    .WithDescription("Returns the ML-Agents training processes currently running via this service.")
    .WithOpenApi();

app.Run();
