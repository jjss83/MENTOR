using System.Diagnostics;
using MentorMlApi.Models;
using MentorMlApi.Options;
using Microsoft.Extensions.Options;

namespace MentorMlApi.Services;

public sealed class MlAgentsRunner(IOptions<MlAgentsSettings> options, ILogger<MlAgentsRunner> logger)
    : IMlAgentsRunner
{
    private readonly MlAgentsSettings _settings = options.Value;
    private readonly ILogger<MlAgentsRunner> _logger = logger;

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

        var runId = string.IsNullOrWhiteSpace(request.RunId)
            ? $"mentor-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}"
            : request.RunId.Trim();

        var noGraphics = request.NoGraphics ?? _settings.DefaultNoGraphics;

        var command = BuildTrainerCommand(configPath, environmentPath, runId, noGraphics, request.AdditionalArguments);
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

        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
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
        string runId,
        bool noGraphics,
        IReadOnlyList<string>? additionalArguments)
    {
        if (string.IsNullOrWhiteSpace(_settings.CondaEnvironmentName))
        {
            throw new InvalidOperationException("Conda environment name is required.");
        }

        var parts = new List<string>
        {
            _settings.CondaExecutable,
            "run",
            "--no-capture-output",
            "-n",
            Quote(_settings.CondaEnvironmentName),
            "mlagents-learn",
            Quote(configPath),
            $"--run-id={runId}"
        };

        if (!string.IsNullOrWhiteSpace(environmentPath))
        {
            parts.Add($"--env={Quote(environmentPath)}");
        }

        if (noGraphics)
        {
            parts.Add("--no-graphics");
        }

        if (additionalArguments is not null)
        {
            parts.AddRange(additionalArguments.Where(a => !string.IsNullOrWhiteSpace(a)));
        }

        return string.Join(' ', parts);
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