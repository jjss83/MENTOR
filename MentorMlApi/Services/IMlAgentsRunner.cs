using MentorMlApi.Models;

namespace MentorMlApi.Services;

public interface IMlAgentsRunner
{
    Task<MlAgentsRunResponse> RunTrainingAsync(MlAgentsRunRequest request, CancellationToken cancellationToken);
}