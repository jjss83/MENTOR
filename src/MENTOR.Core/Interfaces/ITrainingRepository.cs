using MENTOR.Core.Models;

namespace MENTOR.Core.Interfaces;

/// <summary>
/// Repository interface for persisting and retrieving training sessions.
/// </summary>
public interface ITrainingRepository
{
    /// <summary>
    /// Creates a new training session in the database.
    /// </summary>
    /// <param name="session">The training session to create</param>
    /// <returns>The created training session with any generated values</returns>
    Task<TrainingSession> CreateAsync(TrainingSession session);

    /// <summary>
    /// Retrieves a training session by its unique identifier.
    /// </summary>
    /// <param name="id">The session ID</param>
    /// <returns>The training session if found, null otherwise</returns>
    Task<TrainingSession?> GetAsync(Guid id);

    /// <summary>
    /// Retrieves a training session by its run ID.
    /// </summary>
    /// <param name="runId">The run ID</param>
    /// <returns>The training session if found, null otherwise</returns>
    Task<TrainingSession?> GetByRunIdAsync(string runId);

    /// <summary>
    /// Retrieves all training sessions.
    /// </summary>
    /// <returns>Collection of all training sessions</returns>
    Task<IEnumerable<TrainingSession>> GetAllAsync();

    /// <summary>
    /// Retrieves training sessions with a specific status.
    /// </summary>
    /// <param name="status">The status to filter by</param>
    /// <returns>Collection of matching training sessions</returns>
    Task<IEnumerable<TrainingSession>> GetByStatusAsync(TrainingStatus status);

    /// <summary>
    /// Updates an existing training session.
    /// </summary>
    /// <param name="session">The training session to update</param>
    Task UpdateAsync(TrainingSession session);

    /// <summary>
    /// Deletes a training session.
    /// </summary>
    /// <param name="id">The session ID to delete</param>
    Task DeleteAsync(Guid id);
}
