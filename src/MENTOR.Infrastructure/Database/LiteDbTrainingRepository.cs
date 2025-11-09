using LiteDB;
using MENTOR.Core.Interfaces;
using MENTOR.Core.Models;

namespace MENTOR.Infrastructure.Database;

/// <summary>
/// LiteDB implementation of the training repository.
/// </summary>
public class LiteDbTrainingRepository : ITrainingRepository, IDisposable
{
    private readonly LiteDatabase _database;
    private readonly ILiteCollection<TrainingSession> _collection;

    public LiteDbTrainingRepository(string connectionString)
    {
        ArgumentException.ThrowIfNullOrEmpty(connectionString);
        _database = new LiteDatabase(connectionString);
        _collection = _database.GetCollection<TrainingSession>("training_sessions");

        // Create indexes for better query performance
        _collection.EnsureIndex(x => x.RunId);
        _collection.EnsureIndex(x => x.Status);
        _collection.EnsureIndex(x => x.CreatedAt);
    }

    /// <inheritdoc />
    public Task<TrainingSession> CreateAsync(TrainingSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        _collection.Insert(session);
        return Task.FromResult(session);
    }

    /// <inheritdoc />
    public Task<TrainingSession?> GetAsync(Guid id)
    {
        var session = _collection.FindById(id);
        return Task.FromResult<TrainingSession?>(session);
    }

    /// <inheritdoc />
    public Task<TrainingSession?> GetByRunIdAsync(string runId)
    {
        ArgumentException.ThrowIfNullOrEmpty(runId);
        var session = _collection.FindOne(x => x.RunId == runId);
        return Task.FromResult<TrainingSession?>(session);
    }

    /// <inheritdoc />
    public Task<IEnumerable<TrainingSession>> GetAllAsync()
    {
        var sessions = _collection
            .FindAll()
            .OrderByDescending(x => x.CreatedAt)
            .ToList();

        return Task.FromResult<IEnumerable<TrainingSession>>(sessions);
    }

    /// <inheritdoc />
    public Task<IEnumerable<TrainingSession>> GetByStatusAsync(TrainingStatus status)
    {
        var sessions = _collection
            .Find(x => x.Status == status)
            .OrderByDescending(x => x.CreatedAt)
            .ToList();

        return Task.FromResult<IEnumerable<TrainingSession>>(sessions);
    }

    /// <inheritdoc />
    public Task UpdateAsync(TrainingSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        _collection.Update(session);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task DeleteAsync(Guid id)
    {
        _collection.Delete(id);
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _database?.Dispose();
        GC.SuppressFinalize(this);
    }
}
