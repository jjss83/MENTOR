using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using MentorMlApi.Models;

namespace MentorMlApi.Services;

public sealed class MlAgentsProcessTracker : IMlAgentsProcessTracker
{
    private sealed record TrackedProcess(Guid Id, Process Process, string RunId, string Command, string WorkingDirectory, DateTimeOffset StartedAt);

    private readonly ConcurrentDictionary<Guid, TrackedProcess> _running = new();

    public IDisposable Track(Process process, string runId, string command, string workingDirectory, DateTimeOffset startedAt)
    {
        ArgumentNullException.ThrowIfNull(process);
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        if (process.HasExited)
        {
            throw new InvalidOperationException("Cannot track a process that has already exited.");
        }

        var entry = new TrackedProcess(Guid.NewGuid(), process, runId, command, workingDirectory, startedAt);
        _running[entry.Id] = entry;

        return new Tracker(this, entry.Id);
    }

    public IReadOnlyCollection<MlAgentsProcessStatus> GetRunningProcesses()
    {
        var now = DateTimeOffset.UtcNow;
        var statuses = new List<MlAgentsProcessStatus>();

        foreach (var entry in _running.Values)
        {
            if (entry.Process.HasExited)
            {
                _running.TryRemove(entry.Id, out _);
                continue;
            }

            statuses.Add(new MlAgentsProcessStatus(
                entry.Id,
                entry.RunId,
                entry.Process.Id,
                entry.Command,
                entry.WorkingDirectory,
                entry.StartedAt,
                now - entry.StartedAt));
        }

        return statuses;
    }

    private void Remove(Guid id) => _running.TryRemove(id, out _);

    private sealed class Tracker : IDisposable
    {
        private readonly MlAgentsProcessTracker _owner;
        private readonly Guid _id;
        private bool _disposed;

        public Tracker(MlAgentsProcessTracker owner, Guid id)
        {
            _owner = owner;
            _id = id;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _owner.Remove(_id);
            _disposed = true;
        }
    }
}
