using System.Collections.Generic;
using System.Diagnostics;
using MentorMlApi.Models;

namespace MentorMlApi.Services;

public interface IMlAgentsProcessTracker
{
    IDisposable Track(Process process, string runId, string command, string workingDirectory, DateTimeOffset startedAt);
    IReadOnlyCollection<MlAgentsProcessStatus> GetRunningProcesses();
}
