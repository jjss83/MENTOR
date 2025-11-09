using FluentAssertions;
using MENTOR.Core.Interfaces;
using MENTOR.Core.Models;
using MENTOR.Core.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace MENTOR.Core.Tests;

public class TrainingServiceTests
{
    private readonly Mock<ITrainingRepository> _repositoryMock;
    private readonly Mock<ITrainingProcessManager> _processManagerMock;
    private readonly Mock<ILogger<TrainingService>> _loggerMock;
    private readonly TrainingService _service;

    public TrainingServiceTests()
    {
        _repositoryMock = new Mock<ITrainingRepository>();
        _processManagerMock = new Mock<ITrainingProcessManager>();
        _loggerMock = new Mock<ILogger<TrainingService>>();
        _service = new TrainingService(
            _repositoryMock.Object,
            _processManagerMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task StartTrainingAsync_WithValidConfig_ReturnsSuccess()
    {
        // Arrange
        var configPath = Path.GetTempFileName();
        try
        {
            var request = new TrainingRequest(
                ConfigPath: configPath,
                RunId: "test-run");

            var process = new TrainingProcess(Guid.NewGuid(), 12345);

            _repositoryMock
                .Setup(x => x.CreateAsync(It.IsAny<TrainingSession>()))
                .ReturnsAsync((TrainingSession s) => s);

            _processManagerMock
                .Setup(x => x.StartAsync(It.IsAny<TrainingRequest>()))
                .ReturnsAsync(process);

            _repositoryMock
                .Setup(x => x.UpdateAsync(It.IsAny<TrainingSession>()))
                .Returns(Task.CompletedTask);

            // Act
            var result = await _service.StartTrainingAsync(request);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Value.Should().NotBeNull();
            result.Value!.RunId.Should().Be("test-run");
            result.Value.Status.Should().Be(TrainingStatus.Running);
            result.Value.ProcessId.Should().Be(12345);
        }
        finally
        {
            File.Delete(configPath);
        }
    }

    [Fact]
    public async Task StartTrainingAsync_WithMissingConfig_ReturnsFailure()
    {
        // Arrange
        var request = new TrainingRequest(
            ConfigPath: "missing.yaml",
            RunId: "test-run");

        // Act
        var result = await _service.StartTrainingAsync(request);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("not found");
    }

    [Fact]
    public async Task StartTrainingAsync_WithMissingExecutable_ReturnsFailure()
    {
        // Arrange
        var configPath = Path.GetTempFileName();
        try
        {
            var request = new TrainingRequest(
                ConfigPath: configPath,
                RunId: "test-run",
                ExecutablePath: "missing.exe");

            // Act
            var result = await _service.StartTrainingAsync(request);

            // Assert
            result.IsSuccess.Should().BeFalse();
            result.Error.Should().Contain("Executable");
        }
        finally
        {
            File.Delete(configPath);
        }
    }

    [Fact]
    public async Task StopTrainingAsync_WithRunningSession_ReturnsSuccess()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var session = new TrainingSession
        {
            Id = sessionId,
            RunId = "test-run",
            ConfigPath = "test.yaml",
            Status = TrainingStatus.Running,
            CreatedAt = DateTime.UtcNow
        };

        _repositoryMock
            .Setup(x => x.GetAsync(sessionId))
            .ReturnsAsync(session);

        _repositoryMock
            .Setup(x => x.UpdateAsync(It.IsAny<TrainingSession>()))
            .Returns(Task.CompletedTask);

        _processManagerMock
            .Setup(x => x.StopAsync(sessionId))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _service.StopTrainingAsync(sessionId);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeTrue();
    }

    [Fact]
    public async Task StopTrainingAsync_WithNonExistentSession_ReturnsFailure()
    {
        // Arrange
        var sessionId = Guid.NewGuid();

        _repositoryMock
            .Setup(x => x.GetAsync(sessionId))
            .ReturnsAsync((TrainingSession?)null);

        // Act
        var result = await _service.StopTrainingAsync(sessionId);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("not found");
    }

    [Fact]
    public async Task StopTrainingAsync_WithCompletedSession_ReturnsFailure()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var session = new TrainingSession
        {
            Id = sessionId,
            RunId = "test-run",
            ConfigPath = "test.yaml",
            Status = TrainingStatus.Completed,
            CreatedAt = DateTime.UtcNow
        };

        _repositoryMock
            .Setup(x => x.GetAsync(sessionId))
            .ReturnsAsync(session);

        // Act
        var result = await _service.StopTrainingAsync(sessionId);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("not running");
    }

    [Fact]
    public async Task GetSessionAsync_WithExistingId_ReturnsSession()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var session = new TrainingSession
        {
            Id = sessionId,
            RunId = "test-run",
            ConfigPath = "test.yaml",
            Status = TrainingStatus.Running,
            CreatedAt = DateTime.UtcNow
        };

        _repositoryMock
            .Setup(x => x.GetAsync(sessionId))
            .ReturnsAsync(session);

        // Act
        var result = await _service.GetSessionAsync(sessionId);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(sessionId);
    }

    [Fact]
    public async Task GetAllSessionsAsync_ReturnsAllSessions()
    {
        // Arrange
        var sessions = new List<TrainingSession>
        {
            new() { Id = Guid.NewGuid(), RunId = "run-1", ConfigPath = "test1.yaml", Status = TrainingStatus.Running, CreatedAt = DateTime.UtcNow },
            new() { Id = Guid.NewGuid(), RunId = "run-2", ConfigPath = "test2.yaml", Status = TrainingStatus.Completed, CreatedAt = DateTime.UtcNow }
        };

        _repositoryMock
            .Setup(x => x.GetAllAsync())
            .ReturnsAsync(sessions);

        // Act
        var result = await _service.GetAllSessionsAsync();

        // Assert
        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetActiveSessionsAsync_ReturnsOnlyActiveSessions()
    {
        // Arrange
        var sessions = new List<TrainingSession>
        {
            new() { Id = Guid.NewGuid(), RunId = "run-1", ConfigPath = "test1.yaml", Status = TrainingStatus.Running, CreatedAt = DateTime.UtcNow },
            new() { Id = Guid.NewGuid(), RunId = "run-2", ConfigPath = "test2.yaml", Status = TrainingStatus.Queued, CreatedAt = DateTime.UtcNow },
            new() { Id = Guid.NewGuid(), RunId = "run-3", ConfigPath = "test3.yaml", Status = TrainingStatus.Completed, CreatedAt = DateTime.UtcNow }
        };

        _repositoryMock
            .Setup(x => x.GetAllAsync())
            .ReturnsAsync(sessions);

        // Act
        var result = await _service.GetActiveSessionsAsync();

        // Assert
        result.Should().HaveCount(2);
        result.Should().OnlyContain(s => s.Status == TrainingStatus.Running || s.Status == TrainingStatus.Queued);
    }
}
