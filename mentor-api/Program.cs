using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text.Json;
using System.Text.Json.Nodes;
using MentorTrainingRunner;
var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(o => o.AllowSynchronousIO = true);
builder.WebHost.UseUrls("http://localhost:5113");
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
var app = builder.Build();
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
var runStore = new TrainingRunStore();
Console.WriteLine("[Resume] Checking for unfinished training runs...");
var resumeMessages = runStore.ResumeUnfinishedRuns(msg => Console.WriteLine($"[Resume] {msg}"));
if (resumeMessages.Count == 0)
{
    Console.WriteLine("[Resume] No unfinished training runs found.");
}
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
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
app.Run();
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
internal sealed record TrainingRequest(string? ResultsDir, string? CondaEnv, int? BasePort, bool? NoGraphics, bool? SkipConda, bool? Tensorboard, string? EnvPath = null, string? Config = null, string? RunId = null);
internal sealed record CancelRunRequest(string? ResultsDir, string? RunId = null);
internal sealed record TrainingStatusPayload(string RunId, string Status, bool Completed, int? ExitCode, string? ResultsDirectory, string? TrainingStatusPath, string? Message, string? TensorboardUrl, IReadOnlyList<string>? LogTail)
{
    public static TrainingStatusPayload FromFiles(string runId, string resultsDirectory)
    {
        var trainingStatusPath = TrainingRunStore.BuildTrainingStatusPath(resultsDirectory, runId);
        var logPath = TrainingRunStore.BuildLogPath(resultsDirectory, runId);
        var logTail = TrainingRunStore.ReadLogTail(logPath);
        if (File.Exists(trainingStatusPath))
        {
            var statusText = TryReadStatus(trainingStatusPath);
            var normalized = NormalizeStatus(statusText);
            return new TrainingStatusPayload(runId, normalized, Completed: true, ExitCode: null, resultsDirectory, trainingStatusPath, null, TensorboardUrl: null, LogTail: logTail);
        }
        var runDirectory = TrainingRunStore.BuildRunDirectory(resultsDirectory, runId);
        if (Directory.Exists(runDirectory))
        {
            return new TrainingStatusPayload(runId, Status: "unknown", Completed: false, ExitCode: null, resultsDirectory, trainingStatusPath, "Run directory exists but training_status.json has not been written yet.", TensorboardUrl: null, LogTail: logTail);
        }
        return new TrainingStatusPayload(runId, Status: "not-found", Completed: false, ExitCode: null, resultsDirectory, trainingStatusPath, $"No run data found at '{runDirectory}'.", TensorboardUrl: null, LogTail: logTail);
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
internal sealed record TrainingStartResult(bool IsStarted, TrainingRunState? Run, string? Message)
{
    public static TrainingStartResult Started(TrainingRunState run) => new(true, run, null);
    public static TrainingStartResult Conflict(TrainingRunState existing, string? message) => new(false, existing, message);
}
internal sealed class TrainingRunStore
{
    private const int DefaultBasePort = 5005;
    private const int BasePortBlockSize = 20;
    private const int BasePortStride = BasePortBlockSize;
    private const int MaxBasePortProbes = 200;
    private readonly Dictionary<string, TrainingRunState> _runs = new();
    private readonly object _syncRoot = new();
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
            var startResult = TryStart(metadata.ToOptions());
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
    private static string ResolveResultsDirectory(string? resultsDirOverride)
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
    public string RunId { get; }
    public string ResultsDirectory { get; }
    public string LogPath { get; }
    public string? TensorboardUrl { get; }
    public int? BasePort { get; }
    public Task<TrainingRunOutcome> RunTask { get; }
    private TrainingRunState(string runId, string resultsDirectory, string logPath, string? tensorboardUrl, int? basePort, Task<TrainingRunOutcome> runTask, TrainingSessionRunner runner)
    {
        RunId = runId;
        ResultsDirectory = resultsDirectory;
        LogPath = logPath;
        TensorboardUrl = tensorboardUrl;
        BasePort = basePort;
        RunTask = runTask;
        _runner = runner;
        _cancelRequested = false;
    }
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
        var runner = new TrainingSessionRunner(options, outputWriter, outputWriter, outputStream, outputStream, enableConsoleCancel: false);
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
        return new TrainingRunState(options.RunId, options.ResultsDirectory, logPath, options.LaunchTensorBoard ? "http://localhost:6006" : null, options.BasePort, runTask, runner);
    }
    public bool IsCompleted => RunTask.IsCompleted;
    public void Cancel()
    {
        _cancelRequested = true;
        _runner.RequestCancel();
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
        if (_cancelRequested && status == "running")
        {
            status = "canceled";
            message ??= "Cancellation requested.";
        }
        var completed = status != "running";
        var logTail = TrainingRunStore.ReadLogTail(LogPath);
        return new TrainingStatusPayload(RunId, status, completed, exitCode, ResultsDirectory, trainingStatusPath, message, TensorboardUrl, logTail);
    }
}
internal sealed record TrainingRunMetadata(string? EnvPath, string ConfigPath, string RunId, string ResultsDirectory, string CondaEnvironmentName, int? BasePort, bool NoGraphics, bool SkipConda, bool LaunchTensorboard)
{
    private const string MetadataFileName = "run_metadata.json";
    public static void Save(string runDirectory, TrainingOptions options)
    {
        var metadataPath = BuildMetadataPath(runDirectory);
        Directory.CreateDirectory(Path.GetDirectoryName(metadataPath)!);
        var metadata = new TrainingRunMetadata(options.EnvExecutablePath, options.TrainerConfigPath, options.RunId, options.ResultsDirectory, options.CondaEnvironmentName, options.BasePort, options.NoGraphics, options.SkipConda, options.LaunchTensorBoard);
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
        return new TrainingOptions(EnvPath, ConfigPath, RunId, ResultsDirectory, CondaEnvironmentName, BasePort, NoGraphics, SkipConda, LaunchTensorboard);
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
