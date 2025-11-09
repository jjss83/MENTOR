using MENTOR.Core.Interfaces;
using MENTOR.Core.Models;
using Microsoft.Extensions.Logging;

namespace MENTOR.Core.Services;

/// <summary>
/// Core service for managing ML-Agents training sessions.
/// </summary>
public class TrainingService : ITrainingService
{
    private readonly ITrainingRepository _repository;
    private readonly ITrainingProcessManager _processManager;
    private readonly ILogger<TrainingService> _logger;

    public TrainingService(
        ITrainingRepository repository,
        ITrainingProcessManager processManager,
        ILogger<TrainingService> logger)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _processManager = processManager ?? throw new ArgumentNullException(nameof(processManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<Result<TrainingSession>> StartTrainingAsync(TrainingRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        _logger.LogInformation(
            "Starting training session {RunId} with config {ConfigPath}",
            request.RunId,
            request.ConfigPath);

        // Validate configuration file exists
        if (!File.Exists(request.ConfigPath))
        {
            _logger.LogWarning("Configuration file not found: {ConfigPath}", request.ConfigPath);
            return Result<TrainingSession>.Failure($"Configuration file not found: {request.ConfigPath}");
        }

        // Validate executable if provided
        if (!string.IsNullOrEmpty(request.ExecutablePath) && !File.Exists(request.ExecutablePath))
        {
            _logger.LogWarning("Executable file not found: {ExecutablePath}", request.ExecutablePath);
            return Result<TrainingSession>.Failure($"Executable file not found: {request.ExecutablePath}");
        }

        try
        {
            // Create session record
            var session = new TrainingSession
            {
                Id = Guid.NewGuid(),
                RunId = request.RunId,
                ConfigPath = request.ConfigPath,
                ExecutablePath = request.ExecutablePath,
                MaxSteps = request.MaxSteps,
                Status = TrainingStatus.Queued,
                CreatedAt = DateTime.UtcNow,
                Metadata = request.Metadata
            };

            // Save to database
            await _repository.CreateAsync(session);

            // Start the training process
            var process = await _processManager.StartAsync(request);

            // Update session with process information
            session.ProcessId = process.ProcessId;
            session.Status = TrainingStatus.Running;
            session.StartedAt = DateTime.UtcNow;
            await _repository.UpdateAsync(session);

            _logger.LogInformation(
                "Training session {SessionId} started successfully with process ID {ProcessId}",
                session.Id,
                process.ProcessId);

            return Result<TrainingSession>.Success(session);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start training session {RunId}", request.RunId);
            return Result<TrainingSession>.Failure($"Failed to start training: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<Result<bool>> StopTrainingAsync(Guid id)
    {
        _logger.LogInformation("Stopping training session {SessionId}", id);

        var session = await _repository.GetAsync(id);
        if (session == null)
        {
            _logger.LogWarning("Training session {SessionId} not found", id);
            return Result<bool>.Failure("Training session not found");
        }

        if (session.Status != TrainingStatus.Running && session.Status != TrainingStatus.Queued)
        {
            _logger.LogWarning(
                "Training session {SessionId} is not running (status: {Status})",
                id,
                session.Status);
            return Result<bool>.Failure($"Training session is not running (status: {session.Status})");
        }

        try
        {
            // Update status to stopping
            session.Status = TrainingStatus.Stopping;
            await _repository.UpdateAsync(session);

            // Stop the process
            await _processManager.StopAsync(id);

            // Update session as cancelled
            session.Status = TrainingStatus.Cancelled;
            session.EndedAt = DateTime.UtcNow;
            await _repository.UpdateAsync(session);

            _logger.LogInformation("Training session {SessionId} stopped successfully", id);
            return Result<bool>.Success(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to stop training session {SessionId}", id);
            return Result<bool>.Failure($"Failed to stop training: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<TrainingSession?> GetSessionAsync(Guid id)
    {
        return await _repository.GetAsync(id);
    }

    /// <inheritdoc />
    public async Task<TrainingSession?> GetSessionByRunIdAsync(string runId)
    {
        ArgumentException.ThrowIfNullOrEmpty(runId);
        return await _repository.GetByRunIdAsync(runId);
    }

    /// <inheritdoc />
    public async Task<IEnumerable<TrainingSession>> GetAllSessionsAsync()
    {
        return await _repository.GetAllAsync();
    }

    /// <inheritdoc />
    public async Task<IEnumerable<TrainingSession>> GetActiveSessionsAsync()
    {
        var allSessions = await _repository.GetAllAsync();
        return allSessions.Where(s =>
            s.Status == TrainingStatus.Running ||
            s.Status == TrainingStatus.Queued);
    }
}
