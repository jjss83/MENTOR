using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using MentorTrainingRunner;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(o => o.AllowSynchronousIO = true);
builder.WebHost.UseUrls("http://localhost:5113");

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapPost("/train", async (TrainingRequest request, HttpContext context) =>
{
    if (string.IsNullOrWhiteSpace(request.EnvPath) || string.IsNullOrWhiteSpace(request.Config))
    {
        return Results.BadRequest(new { error = "envPath and config are required.", usage = UsageText.GetTrainingUsage() });
    }

    var cliArgs = CliArgs.FromTraining(request).ToArray();
    if (!TrainingOptions.TryParse(cliArgs, out var options, out var error) || options is null)
    {
        return Results.BadRequest(new { error = error ?? "Invalid training options.", usage = UsageText.GetTrainingUsage() });
    }

    context.Response.StatusCode = StatusCodes.Status200OK;
    context.Response.ContentType = "text/plain";
    await context.Response.StartAsync();

    await using var writer = new StreamWriter(context.Response.Body, leaveOpen: true) { AutoFlush = true };
    var runner = new TrainingSessionRunner(options, writer, writer, context.Response.Body, context.Response.Body, enableConsoleCancel: false);
    var exitCode = await runner.RunAsync();
    await writer.WriteLineAsync();
    await writer.WriteLineAsync($"ExitCode: {exitCode}");
    await context.Response.CompleteAsync();

    return Results.Empty;
});

app.MapPost("/report", async (ReportRequest request) =>
{
    if (string.IsNullOrWhiteSpace(request.RunId))
    {
        return Results.BadRequest(new { error = "runId is required.", usage = UsageText.GetReportUsage() });
    }

    var cliArgs = CliArgs.FromReport(request).ToArray();
    if (!ReportOptions.TryParse(cliArgs, out var options, out var error) || options is null)
    {
        return Results.BadRequest(new { error = error ?? "Invalid report options.", usage = UsageText.GetReportUsage() });
    }

    try
    {
        var generator = new TrainingReportGenerator(options);
        var report = await generator.GenerateReportAsync();
        return Results.Json(report, new JsonSerializerOptions { WriteIndented = true });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapPost("/report-interpreter", async (ReportInterpreterRequest request) =>
{
    if (string.IsNullOrWhiteSpace(request.RunId))
    {
        return Results.BadRequest(new { error = "runId is required.", usage = UsageText.GetReportInterpreterUsage() });
    }

    var cliArgs = CliArgs.FromReportInterpreter(request).ToArray();
    if (!ReportInterpreterOptions.TryParse(cliArgs, out var options, out var error) || options is null)
    {
        return Results.BadRequest(new { error = error ?? "Invalid interpreter options.", usage = UsageText.GetReportInterpreterUsage() });
    }

    using var output = new StringWriter();
    using var errors = new StringWriter();
    var runner = new ReportInterpreterRunner(options, output, errors);
    var exitCode = await runner.RunAsync();

    var errorText = errors.ToString();
    if (!string.IsNullOrWhiteSpace(errorText))
    {
        return Results.BadRequest(new { error = errorText.Trim(), usage = UsageText.GetReportInterpreterUsage(), exitCode });
    }

    var payloadText = output.ToString();
    JsonNode? payload = null;
    try
    {
        payload = JsonNode.Parse(payloadText);
    }
    catch
    {
        // If parsing fails, return raw content.
    }

    if (exitCode != 0)
    {
        return Results.BadRequest(new { error = "report-interpreter failed", output = payloadText, exitCode });
    }

    return payload is not null
        ? Results.Json(payload, new JsonSerializerOptions { WriteIndented = true })
        : Results.Text(payloadText, "application/json");
});

app.Run();

internal static class CliArgs
{
    public static List<string> FromTraining(TrainingRequest request)
    {
        var args = new List<string>
        {
            "--env-path", request.EnvPath!,
            "--config", request.Config!,
        };

        if (!string.IsNullOrWhiteSpace(request.RunId))
        {
            args.AddRange(new[] { "--run-id", request.RunId! });
        }

        if (!string.IsNullOrWhiteSpace(request.ResultsDir))
        {
            args.AddRange(new[] { "--results-dir", request.ResultsDir! });
        }

        if (!string.IsNullOrWhiteSpace(request.CondaEnv))
        {
            args.AddRange(new[] { "--conda-env", request.CondaEnv! });
        }

        if (request.BasePort.HasValue)
        {
            args.AddRange(new[] { "--base-port", request.BasePort.Value.ToString(CultureInfo.InvariantCulture) });
        }

        if (request.NoGraphics == true)
        {
            args.Add("--no-graphics");
        }

        if (request.SkipConda == true)
        {
            args.Add("--skip-conda");
        }

        if (request.Tensorboard == true)
        {
            args.Add("--tensorboard");
        }

        return args;
    }

    public static List<string> FromReport(ReportRequest request)
    {
        var args = new List<string> { "--run-id", request.RunId! };

        if (!string.IsNullOrWhiteSpace(request.ResultsDir))
        {
            args.AddRange(new[] { "--results-dir", request.ResultsDir! });
        }

        return args;
    }

    public static List<string> FromReportInterpreter(ReportInterpreterRequest request)
    {
        var args = new List<string> { "--run-id", request.RunId! };

        if (!string.IsNullOrWhiteSpace(request.ResultsDir))
        {
            args.AddRange(new[] { "--results-dir", request.ResultsDir! });
        }

        if (!string.IsNullOrWhiteSpace(request.Prompt))
        {
            args.AddRange(new[] { "--prompt", request.Prompt! });
        }

        if (!string.IsNullOrWhiteSpace(request.OpenAiModel))
        {
            args.AddRange(new[] { "--openai-model", request.OpenAiModel! });
        }

        if (!string.IsNullOrWhiteSpace(request.OpenAiApiKey))
        {
            args.AddRange(new[] { "--openai-api-key", request.OpenAiApiKey! });
        }

        if (request.CheckOpenAi == true)
        {
            args.Add("--check-openai");
        }

        return args;
    }
}

internal sealed record TrainingRequest(
    string? EnvPath,
    string? Config,
    string? RunId,
    string? ResultsDir,
    string? CondaEnv,
    int? BasePort,
    bool? NoGraphics,
    bool? SkipConda,
    bool? Tensorboard);

internal sealed record ReportRequest(string? RunId, string? ResultsDir);

internal sealed record ReportInterpreterRequest(
    string? RunId,
    string? ResultsDir,
    string? Prompt,
    string? OpenAiModel,
    string? OpenAiApiKey,
    bool? CheckOpenAi);

