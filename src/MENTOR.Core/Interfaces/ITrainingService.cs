using MENTOR.Core.Models;

namespace MENTOR.Core.Interfaces;

/// <summary>
/// Service interface for managing training sessions.
/// </summary>
public interface ITrainingService
{
    /// <summary>
    /// Starts a new training session.
    /// </summary>
    /// <param name="request">Training request parameters</param>
    /// <returns>Result containing the created training session or error</returns>
    Task<Result<TrainingSession>> StartTrainingAsync(TrainingRequest request);

    /// <summary>
    /// Stops a running training session.
    /// </summary>
    /// <param name="id">The session ID to stop</param>
    /// <returns>Result indicating success or failure</returns>
    Task<Result<bool>> StopTrainingAsync(Guid id);

    /// <summary>
    /// Gets a specific training session by ID.
    /// </summary>
    /// <param name="id">The session ID</param>
    /// <returns>The training session if found, null otherwise</returns>
    Task<TrainingSession?> GetSessionAsync(Guid id);

    /// <summary>
    /// Gets a specific training session by run ID.
    /// </summary>
    /// <param name="runId">The run ID</param>
    /// <returns>The training session if found, null otherwise</returns>
    Task<TrainingSession?> GetSessionByRunIdAsync(string runId);

    /// <summary>
    /// Gets all training sessions.
    /// </summary>
    /// <returns>Collection of all training sessions</returns>
    Task<IEnumerable<TrainingSession>> GetAllSessionsAsync();

    /// <summary>
    /// Gets all active (running or queued) training sessions.
    /// </summary>
    /// <returns>Collection of active training sessions</returns>
    Task<IEnumerable<TrainingSession>> GetActiveSessionsAsync();
}
