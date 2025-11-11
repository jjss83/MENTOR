using System.Diagnostics;
using MentorMlApi.Models;
using MentorMlApi.Options;
using Microsoft.Extensions.Options;

namespace MentorMlApi.Services;

public sealed class MlAgentsRunner(IOptions<MlAgentsSettings> options, ILogger<MlAgentsRunner> logger, IMlAgentsProcessTracker processTracker)
    : IMlAgentsRunner
{
    private readonly MlAgentsSettings _settings = options.Value;
    private readonly ILogger<MlAgentsRunner> _logger = logger;
    private readonly IMlAgentsProcessTracker _processTracker = processTracker;

    public async Task<MlAgentsRunResponse> RunTrainingAsync(MlAgentsRunRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(_settings.WorkingDirectory))
        {
            throw new InvalidOperationException("WorkingDirectory must be configured before running a job.");
        }

        if (!Directory.Exists(_settings.WorkingDirectory))
        {
            throw new DirectoryNotFoundException($"ML-Agents working directory '{_settings.WorkingDirectory}' was not found.");
        }

        var configPath = ResolvePath(request.ConfigPath ?? _settings.DefaultConfigPath, _settings.WorkingDirectory);
        if (!File.Exists(configPath))
        {
            throw new FileNotFoundException("Unable to locate the trainer configuration file.", configPath);
        }

        var environmentPath = ResolvePath(
            request.EnvironmentPath ?? _settings.DefaultUnityEnvironmentPath,
            _settings.WorkingDirectory,
            required: false);

        var curriculumPath = ResolvePath(request.CurriculumPath, _settings.WorkingDirectory, required: false);
        if (!string.IsNullOrWhiteSpace(curriculumPath) && !File.Exists(curriculumPath))
        {
            throw new FileNotFoundException("Unable to locate the curriculum file.", curriculumPath);
        }

        var runId = string.IsNullOrWhiteSpace(request.RunId)
            ? $"mentor-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}"
            : request.RunId.Trim();

        var noGraphics = request.NoGraphics ?? _settings.DefaultNoGraphics;
        var keepCheckpoints = EnsurePositive(request.KeepCheckpoints, nameof(request.KeepCheckpoints));
        var lesson = EnsureNonNegative(request.Lesson, nameof(request.Lesson));
        var numRuns = EnsurePositive(request.NumRuns, nameof(request.NumRuns));
        var saveFrequency = EnsurePositive(request.SaveFrequency, nameof(request.SaveFrequency));
        var workerId = EnsureNonNegative(request.WorkerId, nameof(request.WorkerId));
        var slow = request.Slow ?? false;
        var loadModel = request.LoadModel ?? false;
        bool? trainMode = request.Train;
        var dockerTargetName = string.IsNullOrWhiteSpace(request.DockerTargetName)
            ? null
            : request.DockerTargetName.Trim();
        var resolvedCurriculumPath = string.IsNullOrWhiteSpace(curriculumPath) ? null : curriculumPath;

        var command = BuildTrainerCommand(
            configPath,
            environmentPath,
            resolvedCurriculumPath,
            runId,
            noGraphics,
            trainMode,
            slow,
            loadModel,
            keepCheckpoints,
            lesson,
            numRuns,
            saveFrequency,
            request.Seed,
            workerId,
            dockerTargetName,
            request.AdditionalArguments);
        var startedAt = DateTimeOffset.UtcNow;

        var stdout = new BoundedBuffer(_settings.MaxOutputLines);
        var stderr = new BoundedBuffer(_settings.MaxOutputLines);

        var startInfo = new ProcessStartInfo(Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe")
        {
            WorkingDirectory = _settings.WorkingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        startInfo.ArgumentList.Add("/c");
        startInfo.ArgumentList.Add(command);

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };

        process.OutputDataReceived += (_, args) => stdout.Add(args.Data);
        process.ErrorDataReceived += (_, args) => stderr.Add(args.Data);

        _logger.LogInformation("Starting ML-Agents run {RunId} with command: {Command}", runId, command);

        IDisposable? trackingScope = null;

        try
        {
            process.Start();
            trackingScope = _processTracker.Track(process, runId, command, _settings.WorkingDirectory, startedAt);
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }
        finally
        {
            trackingScope?.Dispose();
        }

        var completedAt = DateTimeOffset.UtcNow;

        _logger.LogInformation(
            "ML-Agents run {RunId} exited with code {ExitCode} after {Duration}s",
            runId,
            process.ExitCode,
            (completedAt - startedAt).TotalSeconds);

        return new MlAgentsRunResponse(
            command,
            _settings.WorkingDirectory,
            process.ExitCode,
            startedAt,
            completedAt,
            stdout.ToArray(),
            stderr.ToArray());
    }

    private string BuildTrainerCommand(
        string configPath,
        string? environmentPath,
        string? curriculumPath,
        string runId,
        bool noGraphics,
        bool? trainMode,
        bool slow,
        bool loadModel,
        int? keepCheckpoints,
        int? lesson,
        int? numRuns,
        int? saveFrequency,
        int? seed,
        int? workerId,
        string? dockerTargetName,
        IReadOnlyList<string>? additionalArguments)
    {
        var parts = new List<string>();

        if (_settings.UseCondaRun)
        {
            if (string.IsNullOrWhiteSpace(_settings.CondaExecutable))
            {
                throw new InvalidOperationException("CondaExecutable must be set when UseCondaRun is true.");
            }

            if (string.IsNullOrWhiteSpace(_settings.CondaEnvironmentName))
            {
                throw new InvalidOperationException("Conda environment name is required when UseCondaRun is true.");
            }

            parts.Add(_settings.CondaExecutable);
            parts.Add("run");
            parts.Add("--no-capture-output");
            parts.Add("-n");
            parts.Add(Quote(_settings.CondaEnvironmentName));
        }

        parts.Add("mlagents-learn");
        parts.Add(Quote(configPath));
        parts.Add($"--run-id={runId}");

        if (!string.IsNullOrWhiteSpace(environmentPath))
        {
            parts.Add($"--env={Quote(environmentPath)}");
        }

        if (!string.IsNullOrWhiteSpace(curriculumPath))
        {
            parts.Add($"--curriculum={Quote(curriculumPath)}");
        }

        if (noGraphics)
        {
            parts.Add("--no-graphics");
        }

        if (keepCheckpoints is int keep)
        {
            parts.Add($"--keep-checkpoints={keep}");
        }

        if (lesson is int lessonValue)
        {
            parts.Add($"--lesson={lessonValue}");
        }

        if (loadModel)
        {
            parts.Add("--load");
        }

        if (numRuns is int runs)
        {
            parts.Add($"--num-runs={runs}");
        }

        if (saveFrequency is int freq)
        {
            parts.Add($"--save-freq={freq}");
        }

        if (seed is int seedValue)
        {
            parts.Add($"--seed={seedValue}");
        }

        if (slow)
        {
            parts.Add("--slow");
        }

        if (trainMode is true)
        {
            parts.Add("--train");
        }
        else if (trainMode is false)
        {
            parts.Add("--inference");
        }

        if (workerId is int worker)
        {
            parts.Add($"--worker-id={worker}");
        }

        if (!string.IsNullOrWhiteSpace(dockerTargetName))
        {
            parts.Add($"--docker-target-name={Quote(dockerTargetName)}");
        }

        if (additionalArguments is not null)
        {
            parts.AddRange(additionalArguments.Where(a => !string.IsNullOrWhiteSpace(a)));
        }

        return string.Join(' ', parts);
    }

    private static int? EnsurePositive(int? value, string parameterName)
    {
        if (value is null)
        {
            return null;
        }

        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, $"{parameterName} must be greater than zero.");
        }

        return value;
    }

    private static int? EnsureNonNegative(int? value, string parameterName)
    {
        if (value is null)
        {
            return null;
        }

        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, $"{parameterName} must be zero or greater.");
        }

        return value;
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // ignored on purpose
        }
    }

    private static string ResolvePath(string? path, string root, bool required = true)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            if (required)
            {
                throw new InvalidOperationException("A configuration path is required when no default is set.");
            }

            return string.Empty;
        }

        var candidate = Path.IsPathRooted(path) ? path : Path.Combine(root, path);
        return Path.GetFullPath(candidate);
    }

    private static string Quote(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        return value.Contains(' ') ? $"\"{value}\"" : value;
    }

    private sealed class BoundedBuffer(int capacity)
    {
        private readonly int _capacity = capacity <= 0 ? 200 : capacity;
        private readonly LinkedList<string> _buffer = new();
        private readonly object _gate = new();

        public void Add(string? line)
        {
            if (line is null)
            {
                return;
            }

            lock (_gate)
            {
                _buffer.AddLast(line);
                if (_buffer.Count > _capacity)
                {
                    _buffer.RemoveFirst();
                }
            }
        }

        public IReadOnlyList<string> ToArray()
        {
            lock (_gate)
            {
                return _buffer.ToArray();
            }
        }
    }
}
