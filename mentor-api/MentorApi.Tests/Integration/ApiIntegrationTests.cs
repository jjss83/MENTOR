using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using MentorApi.Tests.Integration;

public class ApiIntegrationTests : IClassFixture<MentorApiWebApplicationFactory>
{
    private readonly HttpClient _client;

    public ApiIntegrationTests(MentorApiWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Health_ReturnsOk()
    {
        var response = await _client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<HealthResponse>();
        payload.Should().NotBeNull();
        payload!.Status.Should().Be("ok");
    }

    [Fact]
    public async Task Train_ReturnsBadRequest_WhenConfigMissing()
    {
        var response = await _client.PostAsJsonAsync("/train", new
        {
            envPath = (string?)null,
            config = "does-not-exist.yaml"
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ResumeFlag_ReturnsBadRequest_WhenRunMissing()
    {
        var response = await _client.PostAsJsonAsync("/train/resume-flag", new
        {
            runId = "missing-run",
            resumeOnStart = true
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task TrainLog_ReturnsNotFound_WhenLogMissing()
    {
        var response = await _client.GetAsync("/train/log/unknown-run");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private sealed record HealthResponse(string Status);
}
