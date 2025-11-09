using MENTOR.Core.Models;

namespace MENTOR.Core.Interfaces;

/// <summary>
/// Interface for managing ML-Agents training processes.
/// </summary>
public interface ITrainingProcessManager
{
    /// <summary>
    /// Starts a new ML-Agents training process.
    /// </summary>
    /// <param name="request">Training configuration</param>
    /// <returns>Information about the started process</returns>
    Task<TrainingProcess> StartAsync(TrainingRequest request);

    /// <summary>
    /// Stops a running training process.
    /// </summary>
    /// <param name="sessionId">The session ID to stop</param>
    Task StopAsync(Guid sessionId);

    /// <summary>
    /// Gets the current status of a training process.
    /// </summary>
    /// <param name="sessionId">The session ID</param>
    /// <returns>The current training status</returns>
    Task<TrainingStatus> GetStatusAsync(Guid sessionId);

    /// <summary>
    /// Checks if a training process is still running.
    /// </summary>
    /// <param name="sessionId">The session ID</param>
    /// <returns>True if running, false otherwise</returns>
    Task<bool> IsRunningAsync(Guid sessionId);
}
