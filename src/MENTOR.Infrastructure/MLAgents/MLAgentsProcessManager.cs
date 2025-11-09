using System.Collections.Concurrent;
using System.Diagnostics;
using MENTOR.Core.Interfaces;
using MENTOR.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MENTOR.Infrastructure.MLAgents;

/// <summary>
/// Manages ML-Agents training processes.
/// </summary>
public class MLAgentsProcessManager : ITrainingProcessManager, IDisposable
{
    private readonly MLAgentsOptions _options;
    private readonly ILogger<MLAgentsProcessManager> _logger;
    private readonly ConcurrentDictionary<Guid, Process> _processes = new();

    public MLAgentsProcessManager(
        IOptions<MLAgentsOptions> options,
        ILogger<MLAgentsProcessManager> logger)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<TrainingProcess> StartAsync(TrainingRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var sessionId = Guid.NewGuid();
        var arguments = BuildArguments(request);

        _logger.LogInformation(
            "Starting ML-Agents process for session {SessionId} with arguments: {Arguments}",
            sessionId,
            arguments);

        var startInfo = new ProcessStartInfo
        {
            FileName = "mlagents-learn",
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(request.ConfigPath) ?? Directory.GetCurrentDirectory()
        };

        var process = new Process { StartInfo = startInfo };

        // Set up output handling
        process.OutputDataReceived += (sender, e) => HandleOutput(sessionId, e.Data);
        process.ErrorDataReceived += (sender, e) => HandleError(sessionId, e.Data);

        process.Exited += (sender, e) => HandleProcessExit(sessionId);
        process.EnableRaisingEvents = true;

        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            _processes[sessionId] = process;

            _logger.LogInformation(
                "ML-Agents process started for session {SessionId} with PID {ProcessId}",
                sessionId,
                process.Id);

            return await Task.FromResult(new TrainingProcess(sessionId, process.Id));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start ML-Agents process for session {SessionId}", sessionId);
            process.Dispose();
            throw;
        }
    }

    /// <inheritdoc />
    public Task StopAsync(Guid sessionId)
    {
        if (!_processes.TryGetValue(sessionId, out var process))
        {
            _logger.LogWarning("Process for session {SessionId} not found", sessionId);
            return Task.CompletedTask;
        }

        try
        {
            if (!process.HasExited)
            {
                _logger.LogInformation("Stopping process {ProcessId} for session {SessionId}", process.Id, sessionId);
                
                // Try graceful shutdown first
                process.CloseMainWindow();
                
                // Wait a bit for graceful shutdown
                if (!process.WaitForExit(5000))
                {
                    // Force kill if necessary
                    _logger.LogWarning("Force killing process {ProcessId} for session {SessionId}", process.Id, sessionId);
                    process.Kill(entireProcessTree: true);
                }

                _logger.LogInformation("Process stopped for session {SessionId}", sessionId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping process for session {SessionId}", sessionId);
        }
        finally
        {
            _processes.TryRemove(sessionId, out _);
            process.Dispose();
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<TrainingStatus> GetStatusAsync(Guid sessionId)
    {
        if (!_processes.TryGetValue(sessionId, out var process))
        {
            return Task.FromResult(TrainingStatus.Completed);
        }

        var status = process.HasExited ? TrainingStatus.Completed : TrainingStatus.Running;
        return Task.FromResult(status);
    }

    /// <inheritdoc />
    public Task<bool> IsRunningAsync(Guid sessionId)
    {
        if (!_processes.TryGetValue(sessionId, out var process))
        {
            return Task.FromResult(false);
        }

        return Task.FromResult(!process.HasExited);
    }

    private string BuildArguments(TrainingRequest request)
    {
        var args = new List<string>
        {
            request.ConfigPath,
            $"--run-id={request.RunId}"
        };

        // Add executable path if provided (build mode)
        if (!string.IsNullOrEmpty(request.ExecutablePath))
        {
            args.Add($"--env={request.ExecutablePath}");
        }

        // Add max steps if not default
        if (request.MaxSteps != 500_000)
        {
            args.Add($"--max-steps={request.MaxSteps}");
        }

        // Add results directory from options
        if (!string.IsNullOrEmpty(_options.ResultsDirectory))
        {
            args.Add($"--results-dir={_options.ResultsDirectory}");
        }

        // Add any additional arguments from configuration
        if (_options.AdditionalArguments.Any())
        {
            args.AddRange(_options.AdditionalArguments);
        }

        return string.Join(" ", args);
    }

    private void HandleOutput(Guid sessionId, string? data)
    {
        if (!string.IsNullOrEmpty(data))
        {
            _logger.LogInformation("[{SessionId}] {Output}", sessionId, data);
        }
    }

    private void HandleError(Guid sessionId, string? data)
    {
        if (!string.IsNullOrEmpty(data))
        {
            _logger.LogError("[{SessionId}] {Error}", sessionId, data);
        }
    }

    private void HandleProcessExit(Guid sessionId)
    {
        _logger.LogInformation("Process exited for session {SessionId}", sessionId);
        
        if (_processes.TryRemove(sessionId, out var process))
        {
            process.Dispose();
        }
    }

    public void Dispose()
    {
        foreach (var kvp in _processes)
        {
            try
            {
                if (!kvp.Value.HasExited)
                {
                    kvp.Value.Kill(entireProcessTree: true);
                }
                kvp.Value.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disposing process for session {SessionId}", kvp.Key);
            }
        }

        _processes.Clear();
        GC.SuppressFinalize(this);
    }
}
