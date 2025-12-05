using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using YamlDotNet.RepresentationModel;
using MentorTrainingRunner;
using System.Diagnostics;
var builder = WebApplication.CreateBuilder(args);
builder.Configuration
    .AddJsonFile("mentor-settings.json", optional: true, reloadOnChange: true)
    .AddJsonFile("mentor-settings.local.json", optional: true, reloadOnChange: true);
TrainingOptions.SetDefaultResultsDirectory(builder.Configuration["MentorApi:ResultsDirectory"]);
builder.WebHost.ConfigureKestrel(o => o.AllowSynchronousIO = true);
builder.WebHost.UseUrls("http://localhost:5113");
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
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
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
app.UseCors();
var runStore = new TrainingRunStore();
var dashboardHost = new DashboardHost();
var fileBridgeRoot = builder.Configuration["MentorApi:FileBridgeRoot"];
if (string.IsNullOrWhiteSpace(fileBridgeRoot))
{
    Console.WriteLine("[FileBridge] MentorApi:FileBridgeRoot not configured; /files endpoints will reject requests until set.");
}
else
{
    Console.WriteLine($"[FileBridge] File bridge root set to '{fileBridgeRoot}'.");
}
Console.WriteLine("[Resume] Checking for runs marked to resume on start...");
var resumeMessages = runStore.ResumeUnfinishedRuns(msg => Console.WriteLine($"[Resume] {msg}"));
var resumedAny = resumeMessages.Any(msg => msg.Contains("Resumed", StringComparison.OrdinalIgnoreCase));
if (!resumedAny)
{
    Console.WriteLine("[Resume] No runs were resumed. Use the web app to mark runs for resume.");
}
var dashboardStartup = dashboardHost.Start();
if (dashboardStartup.Started || dashboardStartup.AlreadyRunning)
{
    var statusText = dashboardStartup.Started ? "started" : "already running";
    var url = dashboardStartup.Url ?? dashboardHost.Url;
    var message = string.IsNullOrWhiteSpace(dashboardStartup.Message) ? string.Empty : $" {dashboardStartup.Message}";
    Console.WriteLine($"[Dashboard] Web dashboard {statusText} at {url}.{message}");
}
else
{
    var message = string.IsNullOrWhiteSpace(dashboardStartup.Message) ? "Unknown error." : dashboardStartup.Message;
    Console.WriteLine($"[Dashboard] Failed to start web dashboard: {message}");
}
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapGet("/files", async (string path) =>
{
    var resolved = FileBridge.Resolve(fileBridgeRoot, path);
    if (!resolved.Success || string.IsNullOrWhiteSpace(resolved.FullPath))
    {
        return Results.BadRequest(new { error = resolved.Error ?? "Unable to resolve path.", configuredRoot = resolved.Root ?? fileBridgeRoot });
    }

    if (!File.Exists(resolved.FullPath))
    {
        return Results.NotFound(new { error = $"File '{path}' not found.", resolvedPath = resolved.FullPath });
    }

    var content = await File.ReadAllTextAsync(resolved.FullPath, Encoding.UTF8);
    return Results.Text(content, "text/plain");
});
app.MapPut("/files", async (string path, HttpRequest request) =>
{
    var resolved = FileBridge.Resolve(fileBridgeRoot, path);
    if (!resolved.Success || string.IsNullOrWhiteSpace(resolved.FullPath))
    {
        return Results.BadRequest(new { error = resolved.Error ?? "Unable to resolve path.", configuredRoot = resolved.Root ?? fileBridgeRoot });
    }

    string body;
    using (var reader = new StreamReader(request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 1024, leaveOpen: false))
    {
        body = await reader.ReadToEndAsync();
    }

    Directory.CreateDirectory(Path.GetDirectoryName(resolved.FullPath)!);
    await File.WriteAllTextAsync(resolved.FullPath, body, Encoding.UTF8);

    return Results.Ok(new { path = path.Trim(), resolvedPath = resolved.FullPath, bytesWritten = Encoding.UTF8.GetByteCount(body) });
});
app.MapPost("/train", (TrainingRequest request) =>
{
    var resolvedEnvPath = string.IsNullOrWhiteSpace(request.EnvPath) ? null : request.EnvPath;
    var resolvedConfig = string.IsNullOrWhiteSpace(request.Config) ? "config/ppo/3DBall.yaml" : request.Config;
    var resolvedRunId = string.IsNullOrWhiteSpace(request.RunId) ? null : request.RunId;
    var cliArgs = CliArgs.FromTraining(request, resolvedEnvPath, resolvedConfig, resolvedRunId).ToArray();
    if (!TrainingOptions.TryParse(cliArgs, out var options, out var error) || options is null)
    {
        return Results.BadRequest(new { error = error ?? "Invalid training options.", usage = UsageText.GetTrainingUsage() });
    }
    var startResult = runStore.TryStart(options);
    if (!startResult.IsStarted || startResult.Run is null)
    {
        return Results.Conflict(new { error = startResult.Message ?? $"A training session with runId '{options.RunId}' is already running.", runId = options.RunId });
    }
    return Results.Ok(new { success = true, runId = startResult.Run.RunId, status = "running", resultsDirectory = startResult.Run.ResultsDirectory, logPath = startResult.Run.LogPath, tensorboardUrl = startResult.Run.TensorboardUrl, basePort = startResult.Run.BasePort });
});
app.MapGet("/train-status", (string? resultsDir) =>
{
    var runs = runStore.ListRuns(resultsDir);
    return Results.Ok(runs);
});
app.MapGet("/train-status/{runId}", (string runId, string? resultsDir) =>
{
    if (string.IsNullOrWhiteSpace(runId))
    {
        return Results.BadRequest(new { error = "runId is required." });
    }
    var status = runStore.GetStatus(runId, resultsDir);
    return Results.Ok(status);
});
app.MapGet("/train/log/{runId}", (string runId, string? resultsDir, long? from) =>
{
    if (string.IsNullOrWhiteSpace(runId))
    {
        return Results.BadRequest(new { error = "runId is required." });
    }

    var start = Math.Max(0, from ?? 0);
    var result = runStore.ReadLog(runId.Trim(), resultsDir, start);
    if (!result.Found)
    {
        return Results.NotFound(new { error = result.Error ?? $"Log for '{runId}' not found.", logPath = result.LogPath });
    }

    return Results.Ok(new
    {
        runId = runId.Trim(),
        logPath = result.LogPath,
        from = result.From,
        to = result.To,
        size = result.Size,
        eof = result.EndOfFile,
        content = result.Content
    });
});
app.MapPost("/train/resume-flag", (ResumeFlagRequest request) =>
{
    if (string.IsNullOrWhiteSpace(request.RunId))
    {
        return Results.BadRequest(new { error = "runId is required." });
    }
    var result = runStore.SetResumeOnStart(request.RunId.Trim(), request.ResumeOnStart, request.ResultsDir);
    if (!result.Success)
    {
        return Results.BadRequest(new { error = result.Message ?? "Unable to update resume flag." });
    }
    return Results.Ok(new { runId = request.RunId, resumeOnStart = result.ResumeOnStart, message = result.Message });
});
app.MapPost("/train/stop", (StopRunRequest request) =>
{
    if (string.IsNullOrWhiteSpace(request.RunId))
    {
        return Results.BadRequest(new { error = "runId is required." });
    }

    var result = runStore.Stop(request.RunId.Trim(), request.ResultsDir);
    return result.Status switch
    {
        StopRunResultStatus.NotFound => Results.NotFound(new { error = result.Message ?? $"Run '{request.RunId}' was not found.", runId = request.RunId }),
        StopRunResultStatus.AlreadyCompleted => Results.BadRequest(new { error = result.Message ?? $"Run '{request.RunId}' is already completed.", runId = request.RunId }),
        StopRunResultStatus.AlreadyStopping => Results.Ok(new { runId = request.RunId, stopping = true, message = result.Message ?? "Stop already requested." }),
        _ => Results.Ok(new { runId = request.RunId, stopping = true, message = result.Message ?? "Stop requested; training will exit shortly and can be resumed." })
    };
});
app.MapPost("/train/resume", (ResumeRunRequest request) =>
{
    if (string.IsNullOrWhiteSpace(request.RunId))
    {
        return Results.BadRequest(new { error = "runId is required." });
    }

    var result = runStore.Resume(request.RunId.Trim(), request.ResultsDir);
    if (!result.IsStarted || result.Run is null)
    {
        return Results.BadRequest(new { error = result.Message ?? $"Unable to resume '{request.RunId}'.", runId = request.RunId });
    }

    return Results.Ok(new { success = true, runId = result.Run.RunId, status = "running", resultsDirectory = result.Run.ResultsDirectory, logPath = result.Run.LogPath, tensorboardUrl = result.Run.TensorboardUrl, basePort = result.Run.BasePort, message = result.Message ?? "Resumed run.", resume = true });
});
app.MapGet("/process-status", (string? resultsDir) =>
{
    var status = ProcessStatusReader.Read(resultsDir);
    return Results.Ok(status);
});
app.MapGet("/dashboard/status", () =>
{
    var status = dashboardHost.GetStatus();
    return Results.Ok(status);
});
app.MapGet("/dashboard/start", () =>
{
    var result = dashboardHost.Start();
    if (!result.Started && !result.AlreadyRunning)
    {
        return Results.BadRequest(new { error = result.Message ?? "Unable to start dashboard." });
    }
    return Results.Ok(new { started = result.Started, alreadyRunning = result.AlreadyRunning, url = result.Url ?? dashboardHost.Url, message = result.Message, processId = result.ProcessId });
});
app.MapPost("/dashboard/stop", () =>
{
    var result = dashboardHost.Stop();
    if (!result.Stopped && !result.AlreadyStopped)
    {
        return Results.BadRequest(new { error = result.Message ?? "Unable to stop dashboard." });
    }
    return Results.Ok(new { stopped = result.Stopped, alreadyStopped = result.AlreadyStopped, message = result.Message });
});
app.MapPost("/process-kill", (KillProcessRequest request) =>
{
    var result = ProcessStatusReader.Kill(request);
    if (result.Errors.Count > 0 && result.KilledProcesses == 0 && result.MatchedProcesses == 0)
    {
        return Results.BadRequest(result);
    }
    return Results.Ok(result);
});
app.MapPost("/train/archive", (ArchiveRunRequest request) =>
{
    if (string.IsNullOrWhiteSpace(request.RunId))
    {
        return Results.BadRequest(new { error = "runId is required." });
    }

    var result = runStore.ArchiveRun(request.RunId, request.ResultsDir);
    if (!result.Success)
    {
        return Results.BadRequest(new { error = result.Message ?? "Unable to archive run.", runId = request.RunId });
    }

    return Results.Ok(new { success = true, runId = request.RunId, archivedTo = result.ArchivedTo });
});
app.MapPost("/train/delete", (DeleteRunRequest request) =>
{
    var result = runStore.DeleteRun(request);
    if (!result.Success)
    {
        return Results.BadRequest(new { error = result.Message ?? $"Unable to delete '{request.RunId}'.", confirmRequired = result.ConfirmRequired });
    }

    return Results.Ok(new { deleted = true, runId = request.RunId, deletedFrom = result.DeletedFrom, message = result.Message });
});
app.MapGet("/tensorboard/start", () =>
{
    var request = new StartTensorboardRequest(null, null, null, null, null);
    var result = runStore.StartTensorboard(request);
    if (!result.Started && !result.AlreadyRunning)
    {
        return Results.BadRequest(new { error = result.Message ?? "Unable to start TensorBoard." });
    }
    return Results.Ok(new { started = result.Started, alreadyRunning = result.AlreadyRunning, url = result.Url, message = result.Message });
});


app.Lifetime.ApplicationStopping.Register(() => dashboardHost.Stop());
app.Run();
internal static class FileBridge
{
    public static FilePathResolution Resolve(string? rootDirectory, string requestedPath)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory))
        {
            return new FilePathResolution(false, "MentorApi:FileBridgeRoot is not configured.", null, null);
        }

        var normalizedRequest = requestedPath.Trim();
        if (string.IsNullOrWhiteSpace(normalizedRequest))
        {
            return new FilePathResolution(false, "path is required.", null, EnsureTrailingSeparator(Path.GetFullPath(rootDirectory)));
        }

        var root = EnsureTrailingSeparator(Path.GetFullPath(rootDirectory));
        if (!Directory.Exists(root))
        {
            return new FilePathResolution(false, $"Configured file bridge root does not exist: '{root}'.", null, root);
        }

        var combined = Path.GetFullPath(Path.Combine(root, normalizedRequest));
        if (!combined.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            return new FilePathResolution(false, "Requested path escapes the configured root directory.", combined, root);
        }

        return new FilePathResolution(true, null, combined, root);
    }

    private static string EnsureTrailingSeparator(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return path;
        }

        if (path.EndsWith(Path.DirectorySeparatorChar) || path.EndsWith(Path.AltDirectorySeparatorChar))
        {
            return path;
        }

        return path + Path.DirectorySeparatorChar;
    }
}
internal sealed record FilePathResolution(bool Success, string? Error, string? FullPath, string? Root);
internal static class CliArgs
{
    public static List<string> FromTraining(TrainingRequest request, string? envPathOverride = null, string? configOverride = null, string? runIdOverride = null)
    {
        var envPath = envPathOverride ?? request.EnvPath;
        var config = configOverride ?? request.Config;
        var args = new List<string>();
        if (!string.IsNullOrWhiteSpace(envPath))
        {
            args.AddRange(new[] { "--env-path", envPath! });
        }
        if (!string.IsNullOrWhiteSpace(config))
        {
            args.AddRange(new[] { "--config", config! });
        }
        if (!string.IsNullOrWhiteSpace(runIdOverride))
        {
            args.AddRange(new[] { "--run-id", runIdOverride! });
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
        if (request.Resume == true)
        {
            args.Add("--resume");
        }
        return args;
    }
}
internal enum CancelRunResultStatus
{
    Success,
    NotFound,
    AlreadyCompleted
}
internal sealed record CancelRunResult(CancelRunResultStatus Status, string? Message)
{
    public static CancelRunResult Success() => new(CancelRunResultStatus.Success, null);
    public static CancelRunResult NotFound(string? message) => new(CancelRunResultStatus.NotFound, message);
    public static CancelRunResult AlreadyCompleted(string? message) => new(CancelRunResultStatus.AlreadyCompleted, message);
}
internal enum StopRunResultStatus
{
    Stopping,
    NotFound,
    AlreadyCompleted,
    AlreadyStopping
}
internal sealed record StopRunResult(StopRunResultStatus Status, string? Message)
{
    public static StopRunResult Stopping(string? message) => new(StopRunResultStatus.Stopping, message);
    public static StopRunResult NotFound(string? message) => new(StopRunResultStatus.NotFound, message);
    public static StopRunResult AlreadyCompleted(string? message) => new(StopRunResultStatus.AlreadyCompleted, message);
    public static StopRunResult AlreadyStopping(string? message) => new(StopRunResultStatus.AlreadyStopping, message);
}
internal enum StopTensorboardStatus
{
    Stopped,
    NotTracked,
    Failed
}
internal sealed record StopTensorboardResult(StopTensorboardStatus Status, string? Message)
{
    public static StopTensorboardResult Stopped() => new(StopTensorboardStatus.Stopped, null);
    public static StopTensorboardResult NotTracked(string? message) => new(StopTensorboardStatus.NotTracked, message);
    public static StopTensorboardResult Failed(string? message) => new(StopTensorboardStatus.Failed, message);
}
internal sealed record ResumeFlagResult(bool Success, string? Message, bool? ResumeOnStart)
{
    public static ResumeFlagResult Invalid(string? message) => new(false, message, null);
    public static ResumeFlagResult NotFound(string? message) => new(false, message, null);
    public static ResumeFlagResult Updated(bool resumeOnStart, string? message) => new(true, message, resumeOnStart);
}
internal sealed record TrainingRequest(string? ResultsDir, string? CondaEnv, int? BasePort, bool? NoGraphics, bool? SkipConda, bool? Tensorboard, bool? Resume = null, string? EnvPath = null, string? Config = null, string? RunId = null);
internal sealed record CancelRunRequest(string? ResultsDir, string? RunId = null);
internal sealed record StopRunRequest(string RunId, string? ResultsDir = null);
internal sealed record ResumeRunRequest(string RunId, string? ResultsDir = null);
internal sealed record ArchiveRunRequest(string? RunId, string? ResultsDir = null);
internal sealed record DeleteRunRequest(string RunId, bool? Confirm = null, string? ResultsDir = null);
internal sealed record ResumeFlagRequest(string RunId, bool ResumeOnStart, string? ResultsDir);
internal sealed record StartTensorboardRequest(string? ResultsDir, string? RunId = null, string? CondaEnv = null, bool? SkipConda = null, int? Port = null);
internal sealed record StartTensorboardResult(bool Started, bool AlreadyRunning, string? Url, string? Message);
internal sealed record DashboardStatusPayload(bool Running, string Url, string? Message, string? RootDirectory, int Port, int? ProcessId);
internal sealed record DashboardStartResult(bool Started, bool AlreadyRunning, string? Url, string? Message, int? ProcessId);
internal sealed record DashboardStopResult(bool Stopped, bool AlreadyStopped, string? Message);
internal sealed record ArchiveRunResult(bool Success, string? Message, string? ArchivedTo);
internal sealed record DeleteRunResult(bool Success, string? Message, string? DeletedFrom, bool ConfirmRequired)
{
    public static DeleteRunResult RequireConfirmation(string? message) => new(false, message, null, true);
    public static DeleteRunResult NotFound(string? message) => new(false, message, null, false);
    public static DeleteRunResult Failed(string? message) => new(false, message, null, false);
    public static DeleteRunResult Deleted(string deletedFrom, string? message = null) => new(true, message, deletedFrom, false);
}
internal sealed record EnvProcessStatus(string Executable, int Count);
internal sealed record ProcessStatusPayload(string ResultsDirectory, int MlagentsLearnProcesses, IReadOnlyList<string> KnownEnvExecutables, IReadOnlyList<string> RunningEnvExecutables, IReadOnlyList<EnvProcessStatus> RunningEnvProcesses);
internal sealed record KillProcessRequest(string Executable, string? ResultsDir);
internal sealed record KillProcessResult(string RequestedExecutable, int MatchedProcesses, int KilledProcesses, IReadOnlyList<string> TargetProcesses, IReadOnlyList<string> Errors);
internal sealed record CurriculumStage(string Name, double? Threshold, string? Measure, long? MinLessonLength);
internal sealed record CurriculumState(string ParameterName, string? BehaviorName, IReadOnlyList<CurriculumStage> Stages, int? CurrentStageIndex = null);
internal sealed record QuickStatSummary(string Name, string Kind, long? Steps, double? DurationSeconds, double? MeanReward, double? RewardStdDev, double? BestReward, double? LastReward, int? CheckpointCount, double? LastCheckpointTime, CurriculumState? Curriculum);
internal static class QuickStatReader
{
    private sealed record Checkpoint(long? Steps, double? Reward, double? CreationTime);

    public static IReadOnlyList<QuickStatSummary> Build(string runId, string resultsDirectory)
    {
        try
        {
            var runDirectory = TrainingRunStore.BuildRunDirectory(resultsDirectory, runId);
            return BuildFromRunDirectory(runDirectory);
        }
        catch
        {
            return Array.Empty<QuickStatSummary>();
        }
    }

    private static IReadOnlyList<QuickStatSummary> BuildFromRunDirectory(string runDirectory)
    {
        var statusPath = Path.Combine(runDirectory, "run_logs", "training_status.json");
        if (!File.Exists(statusPath))
        {
            return Array.Empty<QuickStatSummary>();
        }

        JsonNode? node;
        try
        {
            using var stream = File.OpenRead(statusPath);
            node = JsonNode.Parse(stream);
        }
        catch
        {
            return Array.Empty<QuickStatSummary>();
        }

        if (node is not JsonObject root)
        {
            return Array.Empty<QuickStatSummary>();
        }

        var curricula = CurriculumConfigReader.TryLoad(runDirectory);
        var stats = new List<QuickStatSummary>();

        foreach (var kvp in root)
        {
            if (string.Equals(kvp.Key, "metadata", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (kvp.Value is not JsonObject behaviorNode)
            {
                continue;
            }

            var checkpoints = ParseCheckpoints(behaviorNode);
            if (checkpoints.Count == 0)
            {
                continue;
            }

            var rewardValues = checkpoints.Where(c => c.Reward.HasValue).Select(c => c.Reward!.Value).ToArray();
            double? meanReward = rewardValues.Length > 0 ? rewardValues.Average() : null;
            double? rewardStd = rewardValues.Length > 1 && meanReward.HasValue
                ? Math.Sqrt(rewardValues.Sum(v => Math.Pow(v - meanReward.Value, 2)) / rewardValues.Length)
                : null;

            long? maxSteps = checkpoints.Where(c => c.Steps.HasValue).Select(c => c.Steps!.Value).DefaultIfEmpty().Max();
            var timeValues = checkpoints.Where(c => c.CreationTime.HasValue).Select(c => c.CreationTime!.Value).ToArray();
            double? durationSeconds = timeValues.Length > 1 ? timeValues.Max() - timeValues.Min() : null;

            var bestReward = rewardValues.Length > 0 ? rewardValues.Max() : (double?)null;
            var last = checkpoints.OrderBy(c => c.CreationTime ?? 0).ThenBy(c => c.Steps ?? 0).LastOrDefault();
            var lastReward = last?.Reward;
            var lastTime = last?.CreationTime;

            var curriculum = TryAttachCurriculum(curricula, kvp.Key, bestReward);
            var kind = curriculum is null ? "behavior" : "curriculum";

            stats.Add(new QuickStatSummary(
                kvp.Key,
                kind,
                maxSteps,
                durationSeconds,
                meanReward,
                rewardStd,
                bestReward,
                lastReward,
                checkpoints.Count,
                lastTime,
                curriculum));
        }

        return stats;
    }

    private static CurriculumState? TryAttachCurriculum(IReadOnlyDictionary<string, CurriculumState> curricula, string behaviorName, double? bestReward)
    {
        if (curricula.Count == 0)
        {
            return null;
        }

        var match = curricula.FirstOrDefault(c => string.Equals(c.Key, behaviorName, StringComparison.OrdinalIgnoreCase));
        var state = match.Value;
        if (state is null)
        {
            return null;
        }

        if (state.Stages.Count == 0 || bestReward is null)
        {
            return state;
        }

        var index = state.Stages
            .Select((stage, idx) => new { stage, idx })
            .Where(x => x.stage.Threshold is null || bestReward.Value >= x.stage.Threshold.Value)
            .Select(x => x.idx)
            .DefaultIfEmpty(0)
            .Max();

        return state with { CurrentStageIndex = index };
    }

    private static List<Checkpoint> ParseCheckpoints(JsonObject behaviorNode)
    {
        var list = new List<Checkpoint>();
        if (behaviorNode["checkpoints"] is JsonArray checkpoints)
        {
            foreach (var entry in checkpoints.OfType<JsonObject>())
            {
                var cp = ToCheckpoint(entry);
                if (cp is not null)
                {
                    list.Add(cp);
                }
            }
        }

        if (behaviorNode["final_checkpoint"] is JsonObject finalCheckpoint)
        {
            var cp = ToCheckpoint(finalCheckpoint);
            if (cp is not null)
            {
                list.Add(cp);
            }
        }

        return list;
    }

    private static Checkpoint? ToCheckpoint(JsonObject node)
    {
        var steps = TryGetLong(node, "steps");
        var reward = TryGetDouble(node, "reward");
        var creation = TryGetDouble(node, "creation_time");
        if (steps is null && reward is null && creation is null)
        {
            return null;
        }

        return new Checkpoint(steps, reward, creation);
    }

    private static double? TryGetDouble(JsonObject node, string key)
    {
        if (node[key] is JsonValue value && value.TryGetValue<double>(out var dbl))
        {
            return dbl;
        }

        if (node[key] is JsonValue alt && alt.TryGetValue<long>(out var lng))
        {
            return Convert.ToDouble(lng, CultureInfo.InvariantCulture);
        }

        return null;
    }

    private static long? TryGetLong(JsonObject node, string key)
    {
        if (node[key] is JsonValue value && value.TryGetValue<long>(out var number))
        {
            return number;
        }

        if (node[key] is JsonValue alt && alt.TryGetValue<double>(out var dbl))
        {
            return Convert.ToInt64(Math.Round(dbl, MidpointRounding.AwayFromZero), CultureInfo.InvariantCulture);
        }

        return null;
    }
}
internal static class CurriculumConfigReader
{
    public static IReadOnlyDictionary<string, CurriculumState> TryLoad(string runDirectory)
    {
        var configPath = ResolveConfigPath(runDirectory);
        if (configPath is null || !File.Exists(configPath))
        {
            return new Dictionary<string, CurriculumState>();
        }

        try
        {
            using var reader = new StreamReader(configPath);
            var yaml = new YamlStream();
            yaml.Load(reader);
            if (yaml.Documents.Count == 0)
            {
                return new Dictionary<string, CurriculumState>();
            }

            if (yaml.Documents[0].RootNode is not YamlMappingNode root)
            {
                return new Dictionary<string, CurriculumState>();
            }

            if (!root.Children.TryGetValue(new YamlScalarNode("environment_parameters"), out var envParametersNode))
            {
                return new Dictionary<string, CurriculumState>();
            }

            if (envParametersNode is not YamlMappingNode envParameters)
            {
                return new Dictionary<string, CurriculumState>();
            }

            var result = new Dictionary<string, CurriculumState>(StringComparer.OrdinalIgnoreCase);
            foreach (var paramEntry in envParameters.Children)
            {
                var parameterName = (paramEntry.Key as YamlScalarNode)?.Value ?? "curriculum";
                if (paramEntry.Value is not YamlMappingNode parameterMap)
                {
                    continue;
                }

                if (!parameterMap.Children.TryGetValue(new YamlScalarNode("curriculum"), out var curriculumNode))
                {
                    continue;
                }

                if (curriculumNode is not YamlSequenceNode curriculumSequence)
                {
                    continue;
                }

                var stages = new List<CurriculumStage>();
                string? behaviorName = null;

                foreach (var stageNode in curriculumSequence.OfType<YamlMappingNode>())
                {
                    var stageName = GetScalar(stageNode, "name") ?? "Stage";
                    var completionCriteria = stageNode.Children.TryGetValue(new YamlScalarNode("completion_criteria"), out var completionNode)
                        ? completionNode as YamlMappingNode
                        : null;
                    var threshold = completionCriteria is null ? null : TryParseDouble(GetScalar(completionCriteria, "threshold"));
                    var measure = completionCriteria is null ? null : GetScalar(completionCriteria, "measure");
                    behaviorName ??= completionCriteria is null ? null : GetScalar(completionCriteria, "behavior");
                    var minLessonLength = completionCriteria is null ? null : TryParseLong(GetScalar(completionCriteria, "min_lesson_length"));

                    stages.Add(new CurriculumStage(stageName, threshold, measure, minLessonLength));
                }

                if (stages.Count == 0)
                {
                    continue;
                }

                var state = new CurriculumState(parameterName, behaviorName ?? parameterName, stages, null);
                result[state.BehaviorName ?? parameterName] = state;
            }

            return result;
        }
        catch
        {
            return new Dictionary<string, CurriculumState>();
        }
    }

    private static string? ResolveConfigPath(string runDirectory)
    {
        var defaultConfigPath = Path.Combine(runDirectory, "configuration.yaml");
        if (File.Exists(defaultConfigPath))
        {
            return defaultConfigPath;
        }

        try
        {
            var metadata = TrainingRunMetadata.TryLoad(runDirectory);
            var configPath = metadata?.ConfigPath;
            if (string.IsNullOrWhiteSpace(configPath))
            {
                return null;
            }

            var expanded = Environment.ExpandEnvironmentVariables(configPath);
            if (string.IsNullOrWhiteSpace(expanded))
            {
                return null;
            }

            if (Path.IsPathRooted(expanded))
            {
                return File.Exists(expanded) ? expanded : null;
            }

            var relativeToRun = Path.Combine(runDirectory, expanded);
            if (File.Exists(relativeToRun))
            {
                return relativeToRun;
            }

            return File.Exists(expanded) ? expanded : null;
        }
        catch
        {
            return null;
        }
    }

    private static string? GetScalar(YamlMappingNode map, string key)
    {
        return map.Children.TryGetValue(new YamlScalarNode(key), out var valueNode)
            ? (valueNode as YamlScalarNode)?.Value
            : null;
    }

    private static double? TryParseDouble(string? text)
    {
        if (double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var value))
        {
            return value;
        }

        return null;
    }

    private static long? TryParseLong(string? text)
    {
        if (long.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var value))
        {
            return value;
        }

        return null;
    }
}
internal sealed record TrainingStatusPayload(string RunId, string Status, bool Completed, int? ExitCode, string? ResultsDirectory, string? TrainingStatusPath, string? Message, string? TensorboardUrl, string? LogPath, IReadOnlyList<string>? LogTail, TrainingRunParameters? Parameters, bool ResumeOnStart, int? ProcessId = null, bool ProcessAlive = false, bool CanResume = false, IReadOnlyList<QuickStatSummary>? QuickStats = null)
{
    public static TrainingStatusPayload FromFiles(string runId, string resultsDirectory)
    {
        var trainingStatusPath = TrainingRunStore.BuildTrainingStatusPath(resultsDirectory, runId);
        var logPath = TrainingRunStore.BuildLogPath(resultsDirectory, runId);
        var logTail = TrainingRunStore.ReadLogTail(logPath);
        var runDirectory = TrainingRunStore.BuildRunDirectory(resultsDirectory, runId);
        var metadata = TrainingRunMetadata.TryLoad(runDirectory);
        var parameters = TrainingRunStore.BuildParametersFromMetadata(metadata);
        var resumeOnStart = metadata?.ResumeOnStart ?? false;
        var processId = metadata?.ProcessId;
        var processAlive = TrainingRunStore.IsKnownTrainingProcessAlive(processId);
        var stopRequested = metadata?.StopRequested ?? false;
        var quickStats = QuickStatReader.Build(runId, resultsDirectory);
        if (stopRequested && !File.Exists(trainingStatusPath))
        {
            return new TrainingStatusPayload(runId, Status: "stopped", Completed: false, ExitCode: null, resultsDirectory, trainingStatusPath, "Training was stopped for later resume.", TensorboardUrl: null, LogPath: logPath, LogTail: logTail, Parameters: parameters, ResumeOnStart: resumeOnStart, ProcessId: processId, ProcessAlive: processAlive, CanResume: true, QuickStats: quickStats);
        }
        if (File.Exists(trainingStatusPath))
        {
            var statusText = TryReadStatus(trainingStatusPath);
            var normalized = NormalizeStatus(statusText);
            return new TrainingStatusPayload(runId, normalized, Completed: true, ExitCode: null, resultsDirectory, trainingStatusPath, null, TensorboardUrl: null, LogPath: logPath, LogTail: logTail, Parameters: parameters, ResumeOnStart: resumeOnStart, ProcessId: processId, ProcessAlive: processAlive, CanResume: false, QuickStats: quickStats);
        }
        if (Directory.Exists(runDirectory))
        {
            var status = processAlive ? "running" : "unknown";
            var message = processAlive
                ? $"Training process detected with PID {processId}."
                : "Run directory exists but training_status.json has not been written yet.";
            return new TrainingStatusPayload(runId, status, Completed: false, ExitCode: null, resultsDirectory, trainingStatusPath, message, TensorboardUrl: null, LogPath: logPath, LogTail: logTail, Parameters: parameters, ResumeOnStart: resumeOnStart, ProcessId: processId, ProcessAlive: processAlive, CanResume: !processAlive, QuickStats: quickStats);
        }
        return new TrainingStatusPayload(runId, Status: "not-found", Completed: false, ExitCode: null, resultsDirectory, trainingStatusPath, $"No run data found at '{runDirectory}'.", TensorboardUrl: null, LogPath: logPath, LogTail: logTail, Parameters: parameters, ResumeOnStart: resumeOnStart, ProcessId: processId, ProcessAlive: processAlive, CanResume: false, QuickStats: quickStats);
    }
    private static string NormalizeStatus(string? status)
    {
        return status?.ToLowerInvariant() switch { "success" => "succeeded", "succeeded" => "succeeded", "completed" => "succeeded", "failure" => "failed", "failed" => "failed", _ => status ?? "completed" };
    }
    private static string? TryReadStatus(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var node = JsonNode.Parse(stream);
            return node?["status"]?.GetValue<string>();
        }
        catch
        {
            return null;
        }
    }
}
internal sealed record TrainingRunParameters(string? EnvPath, string? ConfigPath, string? CondaEnv, bool? NoGraphics, bool? SkipConda, bool? Tensorboard, int? BasePort, bool? HasEnvExecutable, bool ResumeOnStart, bool? Resume, bool? StopRequested);
internal sealed record TrainingStartResult(bool IsStarted, TrainingRunState? Run, string? Message)
{
    public static TrainingStartResult Started(TrainingRunState run) => new(true, run, null);
    public static TrainingStartResult Conflict(TrainingRunState existing, string? message) => new(false, existing, message);
}
internal sealed class DashboardHost
{
    private readonly object _syncRoot = new();
    private readonly string? _webAppPath;
    private readonly int _port;
    private readonly string _url;
    private Process? _process;

    public string Url => _url;

    public DashboardHost(string? webAppPath = null, int? port = null)
    {
        _port = port ?? 4173;
        _url = $"http://localhost:{_port}";
        _webAppPath = ResolveWebAppDirectory(webAppPath);
    }

    public DashboardStatusPayload GetStatus()
    {
        lock (_syncRoot)
        {
            var running = _process is { HasExited: false };
            var inUse = IsPortInUse(_port);
            var message = running
                ? "Dashboard is running."
                : inUse
                    ? $"Port {_port} is already in use; another dashboard server may be running."
                    : "Dashboard is not running.";

            return new DashboardStatusPayload(
                running || inUse,
                _url,
                message,
                _webAppPath,
                _port,
                running ? _process?.Id : null);
        }
    }

    public DashboardStartResult Start()
    {
        lock (_syncRoot)
        {
            if (_process is { HasExited: false })
            {
                return new DashboardStartResult(false, true, _url, "Dashboard already running.", _process.Id);
            }

            if (_webAppPath is null)
            {
                return new DashboardStartResult(false, false, null, "Unable to locate mentor-webapp directory.", null);
            }

            if (!Directory.Exists(_webAppPath))
            {
                return new DashboardStartResult(false, false, null, $"Dashboard directory not found at '{_webAppPath}'.", null);
            }

            if (IsPortInUse(_port))
            {
                var portMessage = $"Port {_port} is already in use. Assuming dashboard is exposed at {_url}.";
                return new DashboardStartResult(false, true, _url, portMessage, null);
            }

            var process = CreateDashboardProcess(_webAppPath, _port);
            try
            {
                if (!process.Start())
                {
                    return new DashboardStartResult(false, false, null, "Failed to start http-server process.", null);
                }
            }
            catch (Exception ex)
            {
                return new DashboardStartResult(false, false, null, $"Failed to launch dashboard server: {ex.Message}", null);
            }

            _process = process;
            if (process.HasExited)
            {
                var exitCode = process.ExitCode;
                SafeDispose(process);
                _process = null;
                return new DashboardStartResult(false, false, null, $"Dashboard process exited immediately with code {exitCode}.", null);
            }

            process.EnableRaisingEvents = true;
            process.Exited += (_, _) =>
            {
                lock (_syncRoot)
                {
                    if (ReferenceEquals(_process, process))
                    {
                        SafeDispose(_process);
                        _process = null;
                    }
                }
            };

            return new DashboardStartResult(true, false, _url, $"Dashboard exposed from '{_webAppPath}'.", process.Id);
        }
    }

    public DashboardStopResult Stop()
    {
        lock (_syncRoot)
        {
            if (_process is null)
            {
                return new DashboardStopResult(false, true, "Dashboard is not running.");
            }

            try
            {
                if (!_process.HasExited)
                {
                    _process.Kill(entireProcessTree: true);
                    _process.WaitForExit(2000);
                }
            }
            catch (Exception ex)
            {
                return new DashboardStopResult(false, false, $"Unable to stop dashboard: {ex.Message}");
            }
            finally
            {
                SafeDispose(_process);
                _process = null;
            }

            return new DashboardStopResult(true, false, "Dashboard stopped.");
        }
    }

    private static Process CreateDashboardProcess(string webAppPath, int port)
    {
        var startInfo = new ProcessStartInfo
        {
            UseShellExecute = false,
            RedirectStandardOutput = false,
            RedirectStandardError = false,
            CreateNoWindow = true,
            WorkingDirectory = webAppPath
        };

        SetIsolatedTempDirectory(startInfo);

        startInfo.FileName = "npx";
        startInfo.ArgumentList.Add("http-server");
        startInfo.ArgumentList.Add(".");
        startInfo.ArgumentList.Add("-p");
        startInfo.ArgumentList.Add(port.ToString(CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add("-a");
        startInfo.ArgumentList.Add("localhost");
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add("0");

        return new Process { StartInfo = startInfo };
    }

    private static void SafeDispose(Process? process)
    {
        if (process is null)
        {
            return;
        }

        try
        {
            process.Dispose();
        }
        catch
        {
            // ignore dispose failures
        }
    }

    private static void SetIsolatedTempDirectory(ProcessStartInfo startInfo)
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "mentor-api");
        Directory.CreateDirectory(tempRoot);
        var isolatedTemp = Path.Combine(tempRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(isolatedTemp);
        startInfo.Environment["TMP"] = isolatedTemp;
        startInfo.Environment["TEMP"] = isolatedTemp;
        startInfo.Environment["TMPDIR"] = isolatedTemp;
    }

    private static bool IsPortInUse(int port)
    {
        try
        {
            return IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Any(ep => ep.Port == port);
        }
        catch
        {
            return false;
        }
    }

    private static string? ResolveWebAppDirectory(string? overridePath)
    {
        var envOverride = Environment.GetEnvironmentVariable("MENTOR_WEBAPP_DIR");
        var candidates = new[]
        {
            overridePath,
            envOverride,
            Path.Combine(Directory.GetCurrentDirectory(), "mentor-webapp"),
            Path.Combine(Directory.GetCurrentDirectory(), "..", "mentor-webapp"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "mentor-webapp")
        };

        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            try
            {
                var full = Path.GetFullPath(candidate);
                if (Directory.Exists(full))
                {
                    return full;
                }
            }
            catch
            {
                // ignore invalid candidate
            }
        }

        return null;
    }
}
internal sealed class TrainingRunStore
{
    internal const string ArchiveFolderName = "archive";
    private const string ArchiveRootSuffix = "-archive";
    private const int DefaultBasePort = 5005;
    private const int BasePortBlockSize = 20;
    private const int BasePortStride = BasePortBlockSize;
    private const int MaxBasePortProbes = 200;
    private const int MaxLogReadBytes = 256_000;
    private static readonly HashSet<string> AllowedTrainingProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "mlagents-learn",
        "mlagents-learn.exe",
        "conda",
        "conda.exe",
        "python",
        "python.exe",
        "python3",
        "python3.exe"
    };
    private readonly Dictionary<string, TrainingRunState> _runs = new();
    private readonly Dictionary<string, TensorboardInstance> _tensorboards = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _syncRoot = new();
    private sealed record TensorboardInstance(Process Process, string ResultsDirectory, string? RunId, int Port);
    internal sealed record LogReadResult(bool Found, string? LogPath, string Content, long From, long To, long Size, bool EndOfFile, string? Error)
    {
        public static LogReadResult Success(string path, string content, long from, long to, long size, bool eof) => new(true, path, content, from, to, size, eof, null);
        public static LogReadResult NotFound(string? path, string message) => new(false, path, string.Empty, 0, 0, 0, true, message);
        public static LogReadResult Failure(string? path, string message) => new(false, path, string.Empty, 0, 0, 0, true, message);
    }
    public CancelRunResult Cancel(string runId, string? resultsDirOverride)
    {
        lock (_syncRoot)
        {
            if (!_runs.TryGetValue(runId, out var run))
            {
                return CancelRunResult.NotFound(null);
            }
            if (run.IsCompleted)
            {
                return CancelRunResult.AlreadyCompleted(null);
            }
            run.Cancel();
            return CancelRunResult.Success();
        }
    }
    public StopRunResult Stop(string runId, string? resultsDirOverride)
    {
        if (string.IsNullOrWhiteSpace(runId))
        {
            return StopRunResult.NotFound("runId is required.");
        }

        TrainingRunState? run;
        lock (_syncRoot)
        {
            _runs.TryGetValue(runId, out run);
        }

        if (run is null)
        {
            return StopRunResult.NotFound($"Run '{runId}' is not currently tracked.");
        }

        if (run.IsCompleted)
        {
            return StopRunResult.AlreadyCompleted($"Run '{runId}' already finished.");
        }

        if (run.IsStopping)
        {
            return StopRunResult.AlreadyStopping($"Stop was already requested for '{runId}'.");
        }

        var resultsDir = ResolveResultsDirectory(resultsDirOverride ?? run.ResultsDirectory);
        var runDirectory = BuildRunDirectory(resultsDir, runId);
        var metadata = TrainingRunMetadata.TryLoad(runDirectory);
        if (metadata is null)
        {
            TrainingRunMetadata.Save(runDirectory, run.Options);
            metadata = TrainingRunMetadata.TryLoad(runDirectory);
        }
        if (metadata is not null)
        {
            var updated = metadata with { ResumeOnStart = true, StopRequested = true, Resume = true };
            TrainingRunMetadata.Save(runDirectory, updated);
        }

        run.RequestStop();
        return StopRunResult.Stopping($"Stop requested for '{runId}'. Training will exit and can be resumed.");
    }
    public ArchiveRunResult ArchiveRun(string runId, string? resultsDirOverride)
    {
        if (string.IsNullOrWhiteSpace(runId))
        {
            return new ArchiveRunResult(false, "runId is required.", null);
        }

        TrainingRunState? tracked;
        lock (_syncRoot)
        {
            _runs.TryGetValue(runId, out tracked);
        }

        if (tracked is not null && !tracked.IsCompleted)
        {
            return new ArchiveRunResult(false, $"Run '{runId}' is still running. Cancel or wait for completion before archiving.", null);
        }

        var resultsDir = ResolveResultsDirectory(tracked?.ResultsDirectory ?? resultsDirOverride);
        var normalizedResultsDir = NormalizeDirectoryPath(resultsDir);
        var runDirectory = BuildRunDirectory(normalizedResultsDir, runId);
        if (!Directory.Exists(runDirectory))
        {
            return new ArchiveRunResult(false, $"Run '{runId}' not found at '{runDirectory}'.", null);
        }

        var archiveRoot = GetArchiveRoot(normalizedResultsDir);
        var destination = Path.Combine(archiveRoot, runId);
        if (Directory.Exists(destination))
        {
            return new ArchiveRunResult(false, $"Archive target already exists at '{destination}'.", null);
        }

        var stopResult = StopTensorboardForRun(runDirectory);
        if (stopResult.Status == StopTensorboardStatus.Failed)
        {
            return new ArchiveRunResult(false, stopResult.Message ?? "Unable to stop TensorBoard for this run.", null);
        }

        try
        {
            Directory.CreateDirectory(archiveRoot);
            Directory.Move(runDirectory, destination);
        }
        catch (Exception ex)
        {
            if (stopResult.Status == StopTensorboardStatus.NotTracked)
            {
                return new ArchiveRunResult(false, $"Move failed. TensorBoard may still be running for this run. Stop it manually and retry. Details: {ex.Message}", null);
            }

            return new ArchiveRunResult(false, $"Unable to archive run '{runId}': {ex.Message}", null);
        }

        lock (_syncRoot)
        {
            _runs.Remove(runId);
        }

        return new ArchiveRunResult(true, null, destination);
    }
    public DeleteRunResult DeleteRun(DeleteRunRequest request)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.RunId))
        {
            return DeleteRunResult.Failed("runId is required.");
        }

        if (!(request.Confirm ?? false))
        {
            return DeleteRunResult.RequireConfirmation("Deleting a run is destructive. Resubmit with confirm=true to proceed.");
        }

        var runId = request.RunId.Trim();
        TrainingRunState? tracked;
        lock (_syncRoot)
        {
            _runs.TryGetValue(runId, out tracked);
        }

        var resultsDir = ResolveResultsDirectory(tracked?.ResultsDirectory ?? request.ResultsDir);
        var normalizedResultsDir = NormalizeDirectoryPath(resultsDir);
        var runDirectory = BuildRunDirectory(normalizedResultsDir, runId);
        if (!Directory.Exists(runDirectory))
        {
            return DeleteRunResult.NotFound($"Run '{runId}' not found at '{runDirectory}'.");
        }

        var stopResult = StopTensorboardForRun(runDirectory);
        if (stopResult.Status == StopTensorboardStatus.Failed)
        {
            return DeleteRunResult.Failed(stopResult.Message ?? "Unable to stop TensorBoard for this run.");
        }

        var warnings = new List<string>();
        if (tracked is not null && !tracked.IsCompleted)
        {
            tracked.Cancel();
            try
            {
                Task.WaitAny(tracked.RunTask, Task.Delay(TimeSpan.FromSeconds(5)));
            }
            catch
            {
                // ignore wait failures
            }
        }
        else
        {
            var metadata = TrainingRunMetadata.TryLoad(runDirectory);
            TryTerminateKnownTrainingProcess(metadata?.ProcessId, warnings);
        }

        try
        {
            Directory.Delete(runDirectory, recursive: true);
        }
        catch (Exception ex)
        {
            return DeleteRunResult.Failed($"Unable to delete run '{runId}': {ex.Message}");
        }

        lock (_syncRoot)
        {
            _runs.Remove(runId);
        }

        var message = warnings.Count > 0 ? string.Join(" | ", warnings) : null;
        return DeleteRunResult.Deleted(runDirectory, message);
    }
    public ResumeFlagResult SetResumeOnStart(string runId, bool resumeOnStart, string? resultsDirOverride)
    {
        if (string.IsNullOrWhiteSpace(runId))
        {
            return ResumeFlagResult.Invalid("runId is required.");
        }

        var resultsDir = ResolveResultsDirectory(resultsDirOverride);
        var runDirectory = BuildRunDirectory(resultsDir, runId);
        var metadata = TrainingRunMetadata.TryLoad(runDirectory);
        if (metadata is null)
        {
            return ResumeFlagResult.NotFound($"No run metadata found for '{runId}' in '{resultsDir}'.");
        }

        var updated = metadata with { ResumeOnStart = resumeOnStart };
        TrainingRunMetadata.Save(runDirectory, updated);

        var message = resumeOnStart ? $"Run '{runId}' marked to resume on next API start." : $"Run '{runId}' will not automatically resume.";
        return ResumeFlagResult.Updated(resumeOnStart, message);
    }
    public TrainingStartResult TryStart(TrainingOptions options)
    {
        lock (_syncRoot)
        {
            if (_runs.TryGetValue(options.RunId, out var existing) && !existing.IsCompleted)
            {
                return TrainingStartResult.Conflict(existing, $"Training run '{options.RunId}' is already in progress.");
            }
            var resolvedOptions = ResolveBasePort(options, out var portMessage);
            if (resolvedOptions is null)
            {
                return new TrainingStartResult(false, null, portMessage ?? "Unable to find a free base port for training.");
            }
            if (!string.IsNullOrWhiteSpace(portMessage))
            {
                Console.WriteLine($"[Train] {portMessage}");
            }
            var state = TrainingRunState.StartNew(resolvedOptions);
            _runs[resolvedOptions.RunId] = state;
            return new TrainingStartResult(true, state, portMessage);
        }
    }
    public TrainingStartResult Resume(string runId, string? resultsDirOverride)
    {
        if (string.IsNullOrWhiteSpace(runId))
        {
            return new TrainingStartResult(false, null, "runId is required.");
        }

        var resolvedFromRequest = ResolveResultsDirectory(resultsDirOverride);
        var runDirectory = BuildRunDirectory(resolvedFromRequest, runId);
        var metadata = TrainingRunMetadata.TryLoad(runDirectory);
        if (metadata is null)
        {
            return new TrainingStartResult(false, null, $"No run metadata found for '{runId}' in '{resolvedFromRequest}'.");
        }

        var resolvedResultsDir = ResolveResultsDirectory(resultsDirOverride ?? metadata.ResultsDirectory);
        runDirectory = BuildRunDirectory(resolvedResultsDir, runId);
        var options = metadata.ToOptions() with { ResultsDirectory = resolvedResultsDir, Resume = true };
        var startResult = TryStart(options);
        if (startResult.IsStarted)
        {
            var updated = metadata with { StopRequested = false, Resume = true };
            TrainingRunMetadata.Save(runDirectory, updated);
        }

        return startResult;
    }
    public TrainingStatusPayload GetStatus(string runId, string? resultsDirOverride)
    {
        TrainingRunState? tracked;
        lock (_syncRoot)
        {
            _runs.TryGetValue(runId, out tracked);
        }
        if (tracked is not null)
        {
            return tracked.ToPayload();
        }
        var resultsDir = ResolveResultsDirectory(resultsDirOverride);
        return TrainingStatusPayload.FromFiles(runId, resultsDir);
    }
    public IReadOnlyList<TrainingStatusPayload> ListRuns(string? resultsDirOverride = null)
    {
        List<TrainingRunState> tracked;
        lock (_syncRoot)
        {
            tracked = _runs.Values.ToList();
        }
        // Start with any in-memory runs (running or finished) so we return the latest task state.
        var payloads = tracked.Select(run => run.ToPayload()).ToList();
        var resultsDir = ResolveResultsDirectory(resultsDirOverride);
        var knownRunIds = new HashSet<string>(payloads.Select(p => p.RunId), StringComparer.OrdinalIgnoreCase);
        // If training_status.json exists on disk and shows completion (or lacks a status field but exists), prefer that over the in-memory task state.
        for (var i = 0; i < payloads.Count; i++)
        {
            var payload = payloads[i];
            var payloadResultsDir = ResolveResultsDirectory(payload.ResultsDirectory ?? resultsDir);
            var statusPath = BuildTrainingStatusPath(payloadResultsDir, payload.RunId);
            var status = TryReadStatusFromFile(statusPath);
            var completedFromDisk = IsCompletedStatus(status) || (File.Exists(statusPath) && status is null);
            if (completedFromDisk)
            {
                var diskPayload = TrainingStatusPayload.FromFiles(payload.RunId, payloadResultsDir);
                payloads[i] = diskPayload with { TensorboardUrl = diskPayload.TensorboardUrl ?? payload.TensorboardUrl };
                knownRunIds.Add(payload.RunId);
            }
        }
        // Walk the results directory to surface runs that have completed or are only present on disk.
        if (Directory.Exists(resultsDir))
        {
            foreach (var runDirectory in Directory.EnumerateDirectories(resultsDir))
            {
                if (IsArchiveDirectory(runDirectory))
                {
                    continue;
                }
                var runId = Path.GetFileName(runDirectory);
                if (string.IsNullOrWhiteSpace(runId) || knownRunIds.Contains(runId))
                {
                    continue;
                }
                var payload = TrainingStatusPayload.FromFiles(runId, resultsDir);
                payloads.Add(payload);
                knownRunIds.Add(runId);
            }
        }
        return payloads;
    }
    public StartTensorboardResult StartTensorboard(StartTensorboardRequest request)
    {
        var rawResultsDir = string.IsNullOrWhiteSpace(request.ResultsDir) ? TrainingOptions.DefaultResultsDirectory : request.ResultsDir;
        var resultsDir = ResolveResultsDirectory(rawResultsDir);
        var condaEnv = string.IsNullOrWhiteSpace(request.CondaEnv) ? null : request.CondaEnv.Trim();
        var skipConda = request.SkipConda ?? false;
        var tensorboardPort = request.Port ?? 6006;

        if (!string.IsNullOrWhiteSpace(request.RunId))
        {
            var runDirectory = BuildRunDirectory(resultsDir, request.RunId);
            var metadata = TrainingRunMetadata.TryLoad(runDirectory);
            if (metadata is not null)
            {
                resultsDir = ResolveResultsDirectory(metadata.ResultsDirectory);
                condaEnv ??= metadata.CondaEnvironmentName;
                if (!request.SkipConda.HasValue)
                {
                    skipConda = metadata.SkipConda;
                }
            }
        }

        var normalizedResultsDir = NormalizeDirectoryPath(resultsDir);

        lock (_syncRoot)
        {
            PruneExitedTensorboards_NoLock();
            if (_tensorboards.TryGetValue(normalizedResultsDir, out var existing) && !existing.Process.HasExited)
            {
                return new StartTensorboardResult(false, true, $"http://localhost:{existing.Port}", $"TensorBoard already running for '{existing.ResultsDirectory}'.");
            }
        }

        if (!Directory.Exists(normalizedResultsDir))
        {
            Directory.CreateDirectory(normalizedResultsDir);
        }

        var activePorts = GetActiveTcpPorts();
        if (activePorts.Contains(tensorboardPort))
        {
            return new StartTensorboardResult(false, true, $"http://localhost:{tensorboardPort}", $"TensorBoard already running on port {tensorboardPort}.");
        }

        try
        {
            var process = CreateTensorboardProcess(normalizedResultsDir, condaEnv, skipConda, tensorboardPort);
            if (!process.Start())
            {
                return new StartTensorboardResult(false, false, null, "Failed to start TensorBoard process.");
            }

            var earlyExitMessage = ObserveEarlyTensorboardExit(process);
            if (earlyExitMessage is not null)
            {
                SafeDispose(process);
                return new StartTensorboardResult(false, false, null, earlyExitMessage);
            }

            lock (_syncRoot)
            {
                RegisterTensorboard_NoLock(process, normalizedResultsDir, request.RunId, tensorboardPort);
            }

            return new StartTensorboardResult(true, false, $"http://localhost:{tensorboardPort}", $"TensorBoard started on port {tensorboardPort}.");
        }
        catch (Exception ex)
        {
            return new StartTensorboardResult(false, false, null, ex.Message);
        }
    }
    private StopTensorboardResult StopTensorboardForRun(string runDirectory)
    {
        var normalizedRunDir = NormalizeDirectoryPath(runDirectory);

        lock (_syncRoot)
        {
            PruneExitedTensorboards_NoLock();
            foreach (var kvp in _tensorboards.ToArray())
            {
                var instance = kvp.Value;
                if (!IsPathUnderDirectory(normalizedRunDir, instance.ResultsDirectory))
                {
                    continue;
                }

                var process = instance.Process;
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                        process.WaitForExit(TimeSpan.FromSeconds(5));
                    }
                }
                catch (Exception ex)
                {
                    return StopTensorboardResult.Failed($"Unable to stop TensorBoard (PID {process.Id}): {ex.Message}");
                }
                finally
                {
                    RemoveTensorboard_NoLock(kvp.Key, process);
                }

                return StopTensorboardResult.Stopped();
            }
        }

        return StopTensorboardResult.NotTracked("TensorBoard for this run is not managed by mentor-api. Stop it manually and retry.");
    }
    public IReadOnlyList<string> ResumeUnfinishedRuns(Action<string>? log = null, string? resultsDirOverride = null)
    {
        var messages = new List<string>();
        var resultsDir = ResolveResultsDirectory(resultsDirOverride);
        if (!Directory.Exists(resultsDir))
        {
            var msg = $"Results directory '{resultsDir}' does not exist. Nothing to resume.";
            messages.Add(msg);
            log?.Invoke(msg);
            return messages;
        }
        foreach (var runDirectory in Directory.EnumerateDirectories(resultsDir))
        {
            if (IsArchiveDirectory(runDirectory))
            {
                continue;
            }
            var runId = Path.GetFileName(runDirectory);
            var statusPath = BuildTrainingStatusPath(resultsDir, runId);
            var status = TryReadStatusFromFile(statusPath);
            if (IsCompletedStatus(status))
            {
                continue;
            }
            var metadata = TrainingRunMetadata.TryLoad(runDirectory);
            if (metadata is null)
            {
                var skipped = $"Skipped '{runId}' because run_metadata.json is missing or unreadable.";
                messages.Add(skipped);
                log?.Invoke(skipped);
                continue;
            }
            if (!metadata.ResumeOnStart)
            {
                var skipped = $"Skipped '{runId}' because it is not marked to resume on start.";
                messages.Add(skipped);
                log?.Invoke(skipped);
                continue;
            }
            if (IsKnownTrainingProcessAlive(metadata.ProcessId))
            {
                var skipped = $"Skipped '{runId}' because PID {metadata.ProcessId} is still running; not starting another instance.";
                messages.Add(skipped);
                log?.Invoke(skipped);
                continue;
            }
            if (!string.IsNullOrWhiteSpace(metadata.EnvPath))
            {
                if (!File.Exists(metadata.EnvPath) || !string.Equals(Path.GetExtension(metadata.EnvPath), ".exe", StringComparison.OrdinalIgnoreCase))
                {
                    var skipped = $"Skipped '{runId}' because envPath is missing or not a valid .exe ({metadata.EnvPath ?? "<null>"}).";
                    messages.Add(skipped);
                    log?.Invoke(skipped);
                    continue;
                }
            }
            var startResult = Resume(runId, resultsDir);
            var statusLabel = string.IsNullOrWhiteSpace(status) ? "unknown" : status!.ToLowerInvariant();
            if (startResult.IsStarted && startResult.Run is not null)
            {
                var resumed = $"Resumed unfinished training '{runId}' (previous status: {statusLabel}).";
                messages.Add(resumed);
                log?.Invoke(resumed);
                if (string.IsNullOrWhiteSpace(metadata.EnvPath))
                {
                    var manual = $"Run '{runId}' is resuming without envPath; start the Unity Editor or environment manually.";
                    messages.Add(manual);
                    log?.Invoke(manual);
                }
                if (!string.IsNullOrWhiteSpace(startResult.Message))
                {
                    messages.Add(startResult.Message);
                    log?.Invoke(startResult.Message);
                }
            }
            else
            {
                var conflict = startResult.Message ?? $"Training run '{runId}' is already active.";
                messages.Add(conflict);
                log?.Invoke(conflict);
            }
        }
        return messages;
    }
    private static bool IsCompletedStatus(string? status)
    {
        var normalized = status?.ToLowerInvariant();
        return normalized is "succeeded" or "success" or "failed" or "failure" or "completed";
    }
    private static string? TryReadStatusFromFile(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }
        try
        {
            using var stream = File.OpenRead(path);
            var node = JsonNode.Parse(stream);
            return node?["status"]?.GetValue<string>();
        }
        catch
        {
            return null;
        }
    }
    private TrainingOptions? ResolveBasePort(TrainingOptions options, out string? message)
    {
        message = null;
        var desiredBasePort = options.BasePort ?? DefaultBasePort;
        var activePorts = CollectActiveReservedPorts();
        var systemPorts = GetActiveTcpPorts();
        var candidate = FindAvailableBasePort(desiredBasePort, activePorts, systemPorts);
        if (!candidate.HasValue)
        {
            message = $"Could not find a free base port starting at {desiredBasePort}.";
            return null;
        }
        var requested = options.BasePort ?? desiredBasePort;
        if (candidate.Value != requested)
        {
            message = $"Base port {requested} is busy; using {candidate.Value} for run '{options.RunId}'.";
        }
        return options with { BasePort = candidate.Value };
    }
    private int? FindAvailableBasePort(int desiredBasePort, HashSet<int> activePorts, HashSet<int> systemPorts)
    {
        var candidate = desiredBasePort;
        for (var attempt = 0;
 attempt < MaxBasePortProbes;
 attempt++)
        {
            if (IsPortRangeFree(candidate, activePorts, systemPorts))
            {
                return candidate;
            }
            candidate += BasePortStride;
        }
        return null;
    }
    private static bool IsPortRangeFree(int basePort, HashSet<int> activePorts, HashSet<int> systemPorts)
    {
        for (var offset = 0;
 offset < BasePortBlockSize;
 offset++)
        {
            var port = basePort + offset;
            if (activePorts.Contains(port) || systemPorts.Contains(port))
            {
                return false;
            }
        }
        return true;
    }
    private static Process CreateTensorboardProcess(string resultsDir, string? condaEnv, bool skipConda, int port)
    {
        var startInfo = new ProcessStartInfo
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        SetIsolatedTempDirectory(startInfo);

        if (skipConda)
        {
            startInfo.FileName = "tensorboard";
        }
        else
        {
            startInfo.FileName = ResolveCondaExecutable();
            startInfo.ArgumentList.Add("run");
            startInfo.ArgumentList.Add("--live-stream");
            startInfo.ArgumentList.Add("-n");
            startInfo.ArgumentList.Add(string.IsNullOrWhiteSpace(condaEnv) ? "mlagents" : condaEnv);
            startInfo.ArgumentList.Add("tensorboard");
        }

        startInfo.ArgumentList.Add("--logdir");
        startInfo.ArgumentList.Add(resultsDir);

        startInfo.ArgumentList.Add("--port");
        startInfo.ArgumentList.Add(port.ToString(CultureInfo.InvariantCulture));

        startInfo.ArgumentList.Add("--host");
        startInfo.ArgumentList.Add("localhost");

        return new Process { StartInfo = startInfo };
    }

    private static void SetIsolatedTempDirectory(ProcessStartInfo startInfo)
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "mentor-api");
        Directory.CreateDirectory(tempRoot);
        var isolatedTemp = Path.Combine(tempRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(isolatedTemp);
        startInfo.Environment["TMP"] = isolatedTemp;
        startInfo.Environment["TEMP"] = isolatedTemp;
        startInfo.Environment["TMPDIR"] = isolatedTemp;
    }

    private static string ResolveCondaExecutable()
    {
        var fromEnv = Environment.GetEnvironmentVariable("CONDA_EXE");
        if (!string.IsNullOrWhiteSpace(fromEnv) && File.Exists(fromEnv))
        {
            return fromEnv;
        }

        return "conda";
    }

    private HashSet<int> CollectActiveReservedPorts()
    {
        var reserved = new HashSet<int>();
        foreach (var run in _runs.Values)
        {
            if (run.IsCompleted || !run.BasePort.HasValue)
            {
                continue;
            }
            for (var offset = 0;
 offset < BasePortBlockSize;
 offset++)
            {
                reserved.Add(run.BasePort.Value + offset);
            }
        }
        return reserved;
    }
    private static HashSet<int> GetActiveTcpPorts()
    {
        try
        {
            return IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Select(ep => ep.Port).ToHashSet();
        }
        catch
        {
            return new HashSet<int>();
        }
    }
    private static string? ObserveEarlyTensorboardExit(Process process, int waitMilliseconds = 3000)
    {
        var waited = 0;
        while (waited < waitMilliseconds)
        {
            var slice = Math.Min(500, waitMilliseconds - waited);
            if (process.WaitForExit(slice))
            {
                var stderr = string.Empty;
                var stdout = string.Empty;
                try
                {
                    stderr = process.StandardError.ReadToEnd();
                    stdout = process.StandardOutput.ReadToEnd();
                }
                catch
                {
                    // ignore read failures
                }

                var message = $"TensorBoard exited immediately with code {process.ExitCode}.";
                var tail = string.Join(" ", new[] { stdout, stderr }.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()));
                if (!string.IsNullOrWhiteSpace(tail))
                {
                    message += " Output: " + tail;
                }

                return message;
            }

            waited += slice;
        }

        return null;
    }
    private void RegisterTensorboard_NoLock(Process process, string resultsDir, string? runId, int port)
    {
        var key = NormalizeDirectoryPath(resultsDir);
        process.EnableRaisingEvents = true;
        _tensorboards[key] = new TensorboardInstance(process, key, runId, port);
        process.Exited += (_, _) =>
        {
            lock (_syncRoot)
            {
                RemoveTensorboard_NoLock(key, process);
            }
        };
    }
    private void RemoveTensorboard_NoLock(string key, Process? process = null)
    {
        if (_tensorboards.TryGetValue(key, out var existing))
        {
            if (process is null || ReferenceEquals(existing.Process, process))
            {
                _tensorboards.Remove(key);
                SafeDispose(existing.Process);
            }
        }
    }
    private void PruneExitedTensorboards_NoLock()
    {
        foreach (var key in _tensorboards.Where(kvp => kvp.Value.Process.HasExited).Select(kvp => kvp.Key).ToArray())
        {
            RemoveTensorboard_NoLock(key, _tensorboards[key].Process);
        }
    }
    private static void SafeDispose(Process process)
    {
        try
        {
            process.Dispose();
        }
        catch
        {
            // ignore dispose failures
        }
    }
    private static string GetArchiveRoot(string normalizedResultsDir)
    {
        var trimmed = normalizedResultsDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var parent = Path.GetDirectoryName(trimmed);
        var name = Path.GetFileName(trimmed);
        var archiveDirectoryName = string.IsNullOrWhiteSpace(name) ? $"{ArchiveFolderName}{ArchiveRootSuffix}" : $"{name}{ArchiveRootSuffix}";

        return string.IsNullOrWhiteSpace(parent)
            ? Path.Combine(trimmed, archiveDirectoryName)
            : Path.Combine(parent, archiveDirectoryName);
    }
    private static string NormalizeDirectoryPath(string path)
    {
        try
        {
            return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }
    internal static bool IsArchiveDirectory(string path)
    {
        var name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return string.Equals(name, ArchiveFolderName, StringComparison.OrdinalIgnoreCase)
            || (!string.IsNullOrWhiteSpace(name) && name.EndsWith(ArchiveRootSuffix, StringComparison.OrdinalIgnoreCase));
    }

    private void TryTerminateKnownTrainingProcess(int? pid, List<string> warnings)
    {
        if (!pid.HasValue || pid.Value <= 0 || warnings is null)
        {
            return;
        }

        try
        {
            using var process = Process.GetProcessById(pid.Value);
            if (process.HasExited)
            {
                return;
            }

            var name = SafeGetProcessName(process);
            if (!string.IsNullOrWhiteSpace(name) && !AllowedTrainingProcessNames.Contains(name))
            {
                warnings.Add($"PID {pid.Value} not terminated because it is '{name}'.");
                return;
            }

            process.Kill(entireProcessTree: true);
            process.WaitForExit(TimeSpan.FromSeconds(5));
        }
        catch (Exception ex)
        {
            warnings.Add($"PID {pid.GetValueOrDefault()}: {ex.Message}");
        }
    }

    internal static bool IsKnownTrainingProcessAlive(int? pid)
    {
        if (!pid.HasValue || pid.Value <= 0)
        {
            return false;
        }

        try
        {
            using var process = Process.GetProcessById(pid.Value);
            if (process.HasExited)
            {
                return false;
            }

            var name = SafeGetProcessName(process);
            if (string.IsNullOrWhiteSpace(name))
            {
                return true;
            }

            return AllowedTrainingProcessNames.Contains(name);
        }
        catch
        {
            return false;
        }
    }

    private static string? SafeGetProcessName(Process process)
    {
        try
        {
            return process.ProcessName;
        }
        catch
        {
            return null;
        }
    }
    private static bool IsPathUnderDirectory(string path, string parentDirectory)
    {
        if (string.IsNullOrWhiteSpace(parentDirectory))
        {
            return false;
        }

        var normalizedPath = NormalizeDirectoryPath(path);
        var normalizedParent = NormalizeDirectoryPath(parentDirectory);
        var prefix = normalizedParent.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal) ||
                     normalizedParent.EndsWith(Path.AltDirectorySeparatorChar.ToString(), StringComparison.Ordinal)
            ? normalizedParent
            : normalizedParent + Path.DirectorySeparatorChar;

        return normalizedPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normalizedPath, normalizedParent, StringComparison.OrdinalIgnoreCase);
    }
    internal static string ResolveResultsDirectory(string? resultsDirOverride)
    {
        if (!string.IsNullOrWhiteSpace(resultsDirOverride))
        {
            try
            {
                return Path.GetFullPath(resultsDirOverride);
            }
            catch
            {
                return resultsDirOverride;
            }
        }
        try
        {
            return Path.GetFullPath(TrainingOptions.DefaultResultsDirectory);
        }
        catch
        {
            return TrainingOptions.DefaultResultsDirectory;
        }
    }
    internal static TrainingRunParameters? BuildParametersFromOptions(TrainingOptions? options)
    {
        if (options is null)
        {
            return null;
        }
        var envPath = options.EnvExecutablePath;
        return new TrainingRunParameters(envPath, options.TrainerConfigPath, options.CondaEnvironmentName, options.NoGraphics, options.SkipConda, options.LaunchTensorBoard, options.BasePort, HasEnvExecutable: !string.IsNullOrWhiteSpace(envPath), ResumeOnStart: false, Resume: options.Resume, StopRequested: false);
    }
    internal static TrainingRunParameters? BuildParametersFromMetadata(TrainingRunMetadata? metadata)
    {
        if (metadata is null)
        {
            return null;
        }
        var envPath = metadata.EnvPath;
        return new TrainingRunParameters(envPath, metadata.ConfigPath, metadata.CondaEnvironmentName, metadata.NoGraphics, metadata.SkipConda, metadata.LaunchTensorboard, metadata.BasePort, HasEnvExecutable: !string.IsNullOrWhiteSpace(envPath), ResumeOnStart: metadata.ResumeOnStart, Resume: metadata.Resume, StopRequested: metadata.StopRequested);
    }
    public LogReadResult ReadLog(string runId, string? resultsDirOverride, long fromByte = 0, int maxBytes = MaxLogReadBytes)
    {
        if (string.IsNullOrWhiteSpace(runId))
        {
            return LogReadResult.NotFound(null, "runId is required.");
        }

        var normalizedRunId = runId.Trim();
        TrainingRunState? tracked = null;
        lock (_syncRoot)
        {
            _runs.TryGetValue(normalizedRunId, out tracked);
        }

        var resultsDir = ResolveResultsDirectory(resultsDirOverride ?? tracked?.ResultsDirectory);
        var logPath = BuildLogPath(resultsDir, normalizedRunId);
        if (string.IsNullOrWhiteSpace(logPath) || !File.Exists(logPath))
        {
            return LogReadResult.NotFound(logPath, $"Log file not found for '{normalizedRunId}'.");
        }

        try
        {
            using var stream = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var size = stream.Length;
            var safeFrom = Math.Max(0, Math.Min(fromByte, size));
            stream.Seek(safeFrom, SeekOrigin.Begin);
            var remaining = size - safeFrom;
            var boundedMax = Math.Min(Math.Max(0, maxBytes), MaxLogReadBytes);
            var readBytes = (int)Math.Min(boundedMax, remaining);
            var buffer = readBytes > 0 ? new byte[readBytes] : Array.Empty<byte>();
            var count = readBytes > 0 ? stream.Read(buffer, 0, readBytes) : 0;
            var content = count > 0 ? Encoding.UTF8.GetString(buffer, 0, count) : string.Empty;
            var next = safeFrom + count;
            var eof = next >= size;
            return LogReadResult.Success(logPath, content, safeFrom, next, size, eof);
        }
        catch (Exception ex)
        {
            return LogReadResult.Failure(logPath, ex.Message);
        }
    }
    internal static string BuildLogPath(string resultsDirectory, string runId)
    {
        return Path.Combine(resultsDirectory, runId, "run_logs", "mentor-api.log");
    }
    internal static IReadOnlyList<string> ReadLogTail(string? logPath, int lineCount = 10)
    {
        if (string.IsNullOrWhiteSpace(logPath) || !File.Exists(logPath))
        {
            return Array.Empty<string>();
        }
        try
        {
            using var stream = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            var buffer = new Queue<string>(lineCount + 1);
            while (!reader.EndOfStream)
            {
                var line = reader.ReadLine();
                if (line is null)
                {
                    break;
                }
                buffer.Enqueue(line);
                if (buffer.Count > lineCount)
                {
                    buffer.Dequeue();
                }
            }
            return buffer.ToArray();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }
    internal static string BuildTrainingStatusPath(string resultsDirectory, string runId)
    {
        return Path.Combine(resultsDirectory, runId, "run_logs", "training_status.json");
    }
    internal static string BuildRunDirectory(string resultsDirectory, string runId)
    {
        return Path.Combine(resultsDirectory, runId);
    }
}
internal sealed class TrainingRunState
{
    private readonly TrainingSessionRunner _runner;
    private bool _cancelRequested;
    private bool _stopRequested;
    public string RunId { get; }
    public string ResultsDirectory { get; }
    public string LogPath { get; }
    public string? TensorboardUrl { get; }
    public int? BasePort { get; }
    public TrainingOptions Options { get; }
    public Task<TrainingRunOutcome> RunTask { get; }
    private TrainingRunState(string runId, string resultsDirectory, string logPath, string? tensorboardUrl, int? basePort, Task<TrainingRunOutcome> runTask, TrainingSessionRunner runner, TrainingOptions options)
    {
        RunId = runId;
        ResultsDirectory = resultsDirectory;
        LogPath = logPath;
        TensorboardUrl = tensorboardUrl;
        BasePort = basePort;
        RunTask = runTask;
        _runner = runner;
        _cancelRequested = false;
        _stopRequested = false;
        Options = options;
    }
    public bool IsStopping => _stopRequested;
    public static TrainingRunState StartNew(TrainingOptions options)
    {
        var runDirectory = Path.Combine(options.ResultsDirectory, options.RunId);
        var runLogsDirectory = Path.Combine(runDirectory, "run_logs");
        Directory.CreateDirectory(runLogsDirectory);
        TrainingRunMetadata.Save(runDirectory, options);
        var logPath = Path.Combine(runLogsDirectory, "mentor-api.log");
        var outputStream = new FileStream(logPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        var outputWriter = new StreamWriter(outputStream)
        { AutoFlush = true };
        var runner = new TrainingSessionRunner(
            options,
            outputWriter,
            outputWriter,
            outputStream,
            outputStream,
            enableConsoleCancel: false,
            tensorboardPort: null,
            onProcessStarted: pid =>
            {
                try
                {
                    var existing = TrainingRunMetadata.TryLoad(runDirectory);
                    var metadataToSave = existing is null
                        ? new TrainingRunMetadata(options.EnvExecutablePath, options.TrainerConfigPath, options.RunId, options.ResultsDirectory, options.CondaEnvironmentName, options.BasePort, options.NoGraphics, options.SkipConda, options.LaunchTensorBoard, ResumeOnStart: existing?.ResumeOnStart ?? false, ProcessId: pid, StopRequested: false, Resume: options.Resume || (existing?.Resume ?? false))
                        : existing with { ProcessId = pid, StopRequested = false };

                    TrainingRunMetadata.Save(runDirectory, metadataToSave);
                }
                catch
                {
                    // Best-effort; if persisting PID fails we still run training.
                }
            });
        var runTask = Task.Run(async () =>
        {
            try
            {
                var exitCode = await runner.RunAsync().ConfigureAwait(false);
                return TrainingRunOutcome.FromExitCode(exitCode);
            }
            catch (Exception ex)
            {
                await outputWriter.WriteLineAsync($"Error: {ex.Message}").ConfigureAwait(false);
                return TrainingRunOutcome.FromError(ex);
            }
            finally
            {
                await outputWriter.FlushAsync().ConfigureAwait(false);
                await outputStream.FlushAsync().ConfigureAwait(false);
                outputWriter.Dispose();
                await outputStream.DisposeAsync().ConfigureAwait(false);
            }
        });
        return new TrainingRunState(options.RunId, options.ResultsDirectory, logPath, options.LaunchTensorBoard ? "http://localhost:6006" : null, options.BasePort, runTask, runner, options);
    }
    public bool IsCompleted => RunTask.IsCompleted;
    public void Cancel()
    {
        _cancelRequested = true;
        _runner.RequestCancel();
    }
    public void RequestStop()
    {
        _stopRequested = true;
        _cancelRequested = true;
        _runner.RequestStop();
    }
    public TrainingStatusPayload ToPayload()
    {
        var status = "running";
        int? exitCode = null;
        string? message = null;
        if (RunTask.IsCanceled)
        {
            status = "canceled";
            message = "Training was canceled.";
        }
        else if (RunTask.IsFaulted)
        {
            status = "failed";
            message = RunTask.Exception?.GetBaseException().Message ?? "Training failed.";
        }
        else if (RunTask.IsCompletedSuccessfully)
        {
            var outcome = RunTask.Result;
            exitCode = outcome.ExitCode;
            status = outcome.IsSuccess ? "succeeded" : "failed";
            message = outcome.Error?.Message;
        }
        var trainingStatusPath = TrainingRunStore.BuildTrainingStatusPath(ResultsDirectory, RunId);
        if (_stopRequested && status == "running")
        {
            status = "stopping";
            message ??= "Stop requested; waiting for training to exit.";
        }
        else if (_stopRequested && status != "running")
        {
            status = "stopped";
            message ??= "Training stopped for later resume.";
        }
        else if (_cancelRequested && status == "running")
        {
            status = "canceled";
            message ??= "Cancellation requested.";
        }
        var completed = status != "running" && status != "stopping" && status != "stopped";
        var logTail = TrainingRunStore.ReadLogTail(LogPath);
        var runDirectory = TrainingRunStore.BuildRunDirectory(ResultsDirectory, RunId);
        var metadata = TrainingRunMetadata.TryLoad(runDirectory);
        var parameters = TrainingRunStore.BuildParametersFromMetadata(metadata) ?? TrainingRunStore.BuildParametersFromOptions(Options);
        var resumeOnStart = metadata?.ResumeOnStart ?? false;
        var processId = metadata?.ProcessId;
        var processAlive = TrainingRunStore.IsKnownTrainingProcessAlive(processId);
        var canResume = !processAlive && !completed && !string.Equals(status, "running", StringComparison.OrdinalIgnoreCase);
        var quickStats = QuickStatReader.Build(RunId, ResultsDirectory);
        return new TrainingStatusPayload(RunId, status, completed, exitCode, ResultsDirectory, trainingStatusPath, message, TensorboardUrl, LogPath, logTail, parameters, resumeOnStart, processId, processAlive, canResume, quickStats);
    }
}
internal sealed record TrainingRunMetadata(string? EnvPath, string ConfigPath, string RunId, string ResultsDirectory, string CondaEnvironmentName, int? BasePort, bool NoGraphics, bool SkipConda, bool LaunchTensorboard, bool ResumeOnStart = false, int? ProcessId = null, bool StopRequested = false, bool Resume = false)
{
    private const string MetadataFileName = "run_metadata.json";
    public static void Save(string runDirectory, TrainingOptions options)
    {
        var existing = TryLoad(runDirectory);
        var metadata = new TrainingRunMetadata(options.EnvExecutablePath, options.TrainerConfigPath, options.RunId, options.ResultsDirectory, options.CondaEnvironmentName, options.BasePort, options.NoGraphics, options.SkipConda, options.LaunchTensorBoard, ResumeOnStart: existing?.ResumeOnStart ?? false, ProcessId: null, StopRequested: false, Resume: options.Resume || (existing?.Resume ?? false));
        Save(runDirectory, metadata);
    }
    public static void Save(string runDirectory, TrainingRunMetadata metadata)
    {
        var metadataPath = BuildMetadataPath(runDirectory);
        Directory.CreateDirectory(Path.GetDirectoryName(metadataPath)!);
        var json = JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(metadataPath, json);
    }
    public static TrainingRunMetadata? TryLoad(string runDirectory)
    {
        var metadataPath = BuildMetadataPath(runDirectory);
        if (!File.Exists(metadataPath))
        {
            return null;
        }
        try
        {
            var json = File.ReadAllText(metadataPath);
            return JsonSerializer.Deserialize<TrainingRunMetadata>(json);
        }
        catch
        {
            return null;
        }
    }
    public TrainingOptions ToOptions()
    {
        return new TrainingOptions(EnvPath, ConfigPath, RunId, ResultsDirectory, CondaEnvironmentName, BasePort, NoGraphics, SkipConda, LaunchTensorboard, Resume);
    }
    private static string BuildMetadataPath(string runDirectory)
    {
        return Path.Combine(runDirectory, "run_logs", MetadataFileName);
    }
}
internal sealed record TrainingRunOutcome(int? ExitCode, Exception? Error)
{
    public bool IsSuccess => Error is null && ExitCode.GetValueOrDefault() == 0;
    public static TrainingRunOutcome FromExitCode(int exitCode) => new(exitCode, null);
    public static TrainingRunOutcome FromError(Exception ex) => new(null, ex);
}
internal static class ProcessStatusReader
{
    public static ProcessStatusPayload Read(string? resultsDirOverride)
    {
        var resultsDirectory = TrainingRunStore.ResolveResultsDirectory(resultsDirOverride);
        var knownEnvExecutables = DiscoverEnvExecutables(resultsDirectory);
        var (mlagentsCount, runningEnvs) = InspectProcesses(knownEnvExecutables);
        var runningEnvNames = runningEnvs.Select(p => p.Executable).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return new ProcessStatusPayload(resultsDirectory, mlagentsCount, knownEnvExecutables, runningEnvNames, runningEnvs);
    }

    public static KillProcessResult Kill(KillProcessRequest request)
    {
        var errors = new List<string>();
        if (request is null || string.IsNullOrWhiteSpace(request.Executable))
        {
            errors.Add("Executable is required.");
            return new KillProcessResult(string.Empty, 0, 0, Array.Empty<string>(), errors);
        }

        var normalizedTarget = NormalizeExecutableName(request.Executable);
        if (string.IsNullOrWhiteSpace(normalizedTarget))
        {
            errors.Add("Executable is required.");
            return new KillProcessResult(string.Empty, 0, 0, Array.Empty<string>(), errors);
        }

        var resultsDirectory = TrainingRunStore.ResolveResultsDirectory(request.ResultsDir);
        var knownEnvExecutables = DiscoverEnvExecutables(resultsDirectory);
        var allowedTargets = BuildAllowedTargets(knownEnvExecutables);
        if (!allowedTargets.Contains(normalizedTarget))
        {
            var allowedLabel = allowedTargets.Count == 0 ? "mlagents-learn.exe" : string.Join(", ", allowedTargets.OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
            errors.Add($"Executable '{normalizedTarget}' is not recognized. Allowed: {allowedLabel}");
            return new KillProcessResult(normalizedTarget, 0, 0, Array.Empty<string>(), errors);
        }

        var (targetFileName, targetBaseName) = NormalizeTargetParts(normalizedTarget);
        var matched = 0;
        var killed = 0;
        var targetProcesses = new List<string>();

        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (!MatchesTarget(process, targetFileName, targetBaseName))
                {
                    continue;
                }

                matched++;
                targetProcesses.Add($"{SafeGetProcessName(process) ?? "<unknown>"} (PID {process.Id})");
                process.Kill(entireProcessTree: true);
                killed++;
            }
            catch (Exception ex)
            {
                errors.Add($"PID {process.Id}: {ex.Message}");
            }
            finally
            {
                try
                {
                    process.Dispose();
                }
                catch
                {
                    // ignore dispose failures
                }
            }
        }

        return new KillProcessResult(normalizedTarget, matched, killed, targetProcesses, errors);
    }

    private static IReadOnlyList<string> DiscoverEnvExecutables(string resultsDirectory)
    {
        if (!Directory.Exists(resultsDirectory))
        {
            return Array.Empty<string>();
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var runDirectory in Directory.EnumerateDirectories(resultsDirectory))
        {
            if (TrainingRunStore.IsArchiveDirectory(runDirectory))
            {
                continue;
            }
            var metadata = TrainingRunMetadata.TryLoad(runDirectory);
            if (metadata is null || string.IsNullOrWhiteSpace(metadata.EnvPath))
            {
                continue;
            }

            var envPath = metadata.EnvPath.Trim();
            if (!envPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var fileName = Path.GetFileName(envPath);
            if (!string.IsNullOrWhiteSpace(fileName))
            {
                names.Add(fileName);
            }
        }

        return names.ToArray();
    }

    private static (int mlagentsCount, IReadOnlyList<EnvProcessStatus> runningEnvExecutables) InspectProcesses(IReadOnlyCollection<string> envExeNames)
    {
        var mlagentsCount = 0;
        var runningEnvCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        var envFileNames = new HashSet<string>(envExeNames, StringComparer.OrdinalIgnoreCase);
        var envBaseNames = envExeNames
            .Select(name => new { Base = Path.GetFileNameWithoutExtension(name), Full = name })
            .Where(x => !string.IsNullOrWhiteSpace(x.Base) && !string.IsNullOrWhiteSpace(x.Full))
            .ToDictionary(x => x.Base!, x => x.Full!, StringComparer.OrdinalIgnoreCase);

        foreach (var process in Process.GetProcesses())
        {
            try
            {
                var processName = SafeGetProcessName(process);
                if (!string.IsNullOrWhiteSpace(processName) && string.Equals(processName, "mlagents-learn", StringComparison.OrdinalIgnoreCase))
                {
                    mlagentsCount++;
                }

                if (envBaseNames.Count == 0 && envFileNames.Count == 0)
                {
                    continue;
                }

                string? canonicalName = null;

                if (!string.IsNullOrWhiteSpace(processName) && envBaseNames.TryGetValue(processName, out var canonical))
                {
                    canonicalName = canonical;
                }
                else
                {
                    var moduleFile = SafeGetMainModuleFile(process);
                    if (string.IsNullOrWhiteSpace(moduleFile))
                    {
                        continue;
                    }

                    var fileName = Path.GetFileName(moduleFile);
                    if (string.IsNullOrWhiteSpace(fileName))
                    {
                        continue;
                    }

                    if (envFileNames.Contains(fileName))
                    {
                        canonicalName = fileName;
                    }
                    else
                    {
                        var baseName = Path.GetFileNameWithoutExtension(fileName);
                        if (!string.IsNullOrWhiteSpace(baseName) && envBaseNames.TryGetValue(baseName, out var mapped))
                        {
                            canonicalName = mapped;
                        }
                    }
                }

                if (string.IsNullOrWhiteSpace(canonicalName))
                {
                    continue;
                }

                runningEnvCounts.TryGetValue(canonicalName, out var current);
                runningEnvCounts[canonicalName] = current + 1;
            }
            catch
            {
                // Ignore processes that cannot be inspected.
            }
            finally
            {
                try
                {
                    process.Dispose();
                }
                catch
                {
                    // ignore dispose failures
                }
            }
        }

        var running = runningEnvCounts
            .OrderBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)
            .Select(kvp => new EnvProcessStatus(kvp.Key, kvp.Value))
            .ToArray();

        return (mlagentsCount, running);
    }

    private static HashSet<string> BuildAllowedTargets(IEnumerable<string> knownEnvExecutables)
    {
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "mlagents-learn",
            "mlagents-learn.exe",
            "tensorboard",
            "tensorboard.exe"
        };

        foreach (var env in knownEnvExecutables)
        {
            var fileName = Path.GetFileName(env);
            if (!string.IsNullOrWhiteSpace(fileName))
            {
                allowed.Add(fileName);
            }

            var baseName = Path.GetFileNameWithoutExtension(env);
            if (!string.IsNullOrWhiteSpace(baseName))
            {
                allowed.Add(baseName);
            }
        }

        return allowed;
    }

    private static string NormalizeExecutableName(string executable)
    {
        var fileName = Path.GetFileName(executable.Trim());
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return string.Empty;
        }

        return fileName;
    }

    private static (string fileName, string baseName) NormalizeTargetParts(string executable)
    {
        var fileName = NormalizeExecutableName(executable);
        var baseName = Path.GetFileNameWithoutExtension(fileName);
        return (fileName, string.IsNullOrWhiteSpace(baseName) ? fileName : baseName);
    }

    private static bool MatchesTarget(Process process, string targetFileName, string targetBaseName)
    {
        var processName = SafeGetProcessName(process);
        if (!string.IsNullOrWhiteSpace(processName) && string.Equals(processName, targetBaseName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var moduleFile = SafeGetMainModuleFile(process);
        if (string.IsNullOrWhiteSpace(moduleFile))
        {
            return false;
        }

        var fileName = Path.GetFileName(moduleFile);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        if (string.Equals(fileName, targetFileName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var baseName = Path.GetFileNameWithoutExtension(fileName);
        if (!string.IsNullOrWhiteSpace(baseName) && string.Equals(baseName, targetBaseName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private static string? SafeGetProcessName(Process process)
    {
        try
        {
            return process.ProcessName;
        }
        catch
        {
            return null;
        }
    }

    private static string? SafeGetMainModuleFile(Process process)
    {
        try
        {
            return process.MainModule?.FileName;
        }
        catch
        {
            return null;
        }
    }
}
