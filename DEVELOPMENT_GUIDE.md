# MENTOR Development Guide - C# & .NET 8

## Project Overview
**MENTOR** - ML-Ecosystem Navigation for Training Of Reinforcement agents  
A Windows service that automates Unity ML-Agents training workflows using C# and .NET 8.

---

## Technology Stack

### Core Technologies
- **Language**: C# 12 with .NET 8
- **Web API**: ASP.NET Core Minimal APIs
- **Database**: LiteDB (embedded, no-setup database)
- **Process Management**: System.Diagnostics.Process
- **Logging**: Serilog (structured logging)
- **Testing**: xUnit + FluentAssertions
- **IDE**: Visual Studio 2022 or JetBrains Rider

### NuGet Packages
```xml
<!-- Essential only - add to .csproj -->
<PackageReference Include="LiteDB" Version="5.0.17" />
<PackageReference Include="Serilog.AspNetCore" Version="8.0.0" />
<PackageReference Include="FluentValidation" Version="11.9.0" />
```

---

## Project Structure

```
MENTOR/
├── src/
│   ├── MENTOR.Core/              # Business logic (no web dependencies)
│   │   ├── Models/               # Data models
│   │   ├── Services/             # Business services
│   │   ├── Interfaces/           # Service contracts
│   │   └── MENTOR.Core.csproj
│   │
│   ├── MENTOR.API/               # Web API layer
│   │   ├── Endpoints/            # API endpoint definitions
│   │   ├── Program.cs            # Application entry point
│   │   ├── appsettings.json      # Configuration
│   │   └── MENTOR.API.csproj
│   │
│   └── MENTOR.Infrastructure/    # External integrations
│       ├── Database/             # LiteDB repositories
│       ├── MLAgents/             # ML-Agents process management
│       ├── FileSystem/           # File operations
│       └── MENTOR.Infrastructure.csproj
│
├── tests/
│   ├── MENTOR.Core.Tests/
│   └── MENTOR.API.Tests/
│
├── docs/                         # Documentation
├── examples/                     # Example configs
└── MENTOR.sln                   # Solution file
```

---

## Development Principles for C#

### 1. Simplicity First
```csharp
// ✅ GOOD - Clear and simple
public class TrainingSession
{
    public Guid Id { get; init; }
    public string RunId { get; init; }
    public TrainingStatus Status { get; set; }
    public DateTime StartTime { get; init; }
    public DateTime? EndTime { get; set; }
}

// ❌ BAD - Over-engineered
public class TrainingSession<TConfig, TResult> : BaseEntity<Guid>, ITrackable, IValidatable
    where TConfig : IConfiguration
    where TResult : ITrainingResult
{
    // Too complex for our needs
}
```

### 2. Use Records for Immutable Data
```csharp
// Simple, immutable, with automatic equality
public record TrainingRequest(
    string ConfigPath,
    string RunId,
    string? ExecutablePath = null,  // null = Editor mode
    int MaxSteps = 500_000
);

public record TrainingResult(
    string RunId,
    float FinalReward,
    int TotalSteps,
    TimeSpan Duration
);
```

### 3. Explicit Error Handling
```csharp
public class TrainingService
{
    public async Task<Result<TrainingSession>> StartTrainingAsync(TrainingRequest request)
    {
        // Validate first
        if (!File.Exists(request.ConfigPath))
            return Result<TrainingSession>.Failure("Configuration file not found");

        try
        {
            var session = await LaunchTrainingProcessAsync(request);
            return Result<TrainingSession>.Success(session);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Failed to start training");
            return Result<TrainingSession>.Failure($"Training failed: {ex.Message}");
        }
    }
}

// Simple Result pattern
public class Result<T>
{
    public bool IsSuccess { get; }
    public T? Value { get; }
    public string? Error { get; }
    
    public static Result<T> Success(T value) => new(true, value, null);
    public static Result<T> Failure(string error) => new(false, default, error);
}
```

### 4. Configuration with Options Pattern
```csharp
// appsettings.json
{
  "MLAgents": {
    "PythonPath": "python",
    "DefaultTimeout": 3600,
    "ResultsDirectory": "C:/MLAgents/results"
  }
}

// Strongly-typed configuration
public class MLAgentsOptions
{
    public string PythonPath { get; set; } = "python";
    public int DefaultTimeout { get; set; } = 3600;
    public string ResultsDirectory { get; set; } = "C:/MLAgents/results";
}

// In Program.cs
builder.Services.Configure<MLAgentsOptions>(
    builder.Configuration.GetSection("MLAgents"));
```

---

## Core Components Implementation

### 1. Training Process Manager
```csharp
public interface ITrainingProcessManager
{
    Task<TrainingProcess> StartAsync(TrainingRequest request);
    Task StopAsync(Guid sessionId);
    Task<TrainingStatus> GetStatusAsync(Guid sessionId);
}

public class MLAgentsProcessManager : ITrainingProcessManager
{
    private readonly ILogger<MLAgentsProcessManager> _logger;
    private readonly Dictionary<Guid, Process> _processes = new();

    public async Task<TrainingProcess> StartAsync(TrainingRequest request)
    {
        var sessionId = Guid.NewGuid();
        
        // Build command
        var arguments = BuildArguments(request);
        
        var startInfo = new ProcessStartInfo
        {
            FileName = "mlagents-learn",
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        var process = new Process { StartInfo = startInfo };
        
        // Set up output handling
        process.OutputDataReceived += (s, e) => HandleOutput(sessionId, e.Data);
        process.ErrorDataReceived += (s, e) => HandleError(sessionId, e.Data);
        
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        
        _processes[sessionId] = process;
        
        return new TrainingProcess(sessionId, process.Id);
    }
    
    private string BuildArguments(TrainingRequest request)
    {
        var args = $"{request.ConfigPath} --run-id={request.RunId}";
        
        // Add executable path if provided (determines mode)
        if (!string.IsNullOrEmpty(request.ExecutablePath))
            args += $" --env={request.ExecutablePath}";
            
        return args;
    }
}
```

### 2. Database Repository
```csharp
public interface ITrainingRepository
{
    Task<TrainingSession> CreateAsync(TrainingSession session);
    Task<TrainingSession?> GetAsync(Guid id);
    Task<IEnumerable<TrainingSession>> GetAllAsync();
    Task UpdateAsync(TrainingSession session);
}

public class LiteDbTrainingRepository : ITrainingRepository
{
    private readonly LiteDatabase _database;
    
    public LiteDbTrainingRepository(string connectionString)
    {
        _database = new LiteDatabase(connectionString);
    }
    
    public Task<TrainingSession> CreateAsync(TrainingSession session)
    {
        var collection = _database.GetCollection<TrainingSession>();
        collection.Insert(session);
        return Task.FromResult(session);
    }
    
    public Task<TrainingSession?> GetAsync(Guid id)
    {
        var collection = _database.GetCollection<TrainingSession>();
        var session = collection.FindById(id);
        return Task.FromResult(session);
    }
}
```

### 3. API Endpoints
```csharp
// Program.cs - Minimal API approach
var builder = WebApplication.CreateBuilder(args);

// Add services
builder.Services.AddSingleton<ITrainingService, TrainingService>();
builder.Services.AddSingleton<ITrainingRepository>(sp => 
    new LiteDbTrainingRepository("Filename=mentor.db"));

var app = builder.Build();

// Training endpoints
app.MapPost("/api/training/start", async (
    TrainingRequest request,
    ITrainingService service) =>
{
    var result = await service.StartTrainingAsync(request);
    
    return result.IsSuccess
        ? Results.Ok(result.Value)
        : Results.BadRequest(result.Error);
});

app.MapGet("/api/training/{id}/status", async (
    Guid id,
    ITrainingService service) =>
{
    var session = await service.GetSessionAsync(id);
    return session != null
        ? Results.Ok(session)
        : Results.NotFound();
});

app.MapPost("/api/training/{id}/stop", async (
    Guid id,
    ITrainingService service) =>
{
    await service.StopTrainingAsync(id);
    return Results.Ok();
});

app.MapGet("/api/training", async (ITrainingService service) =>
{
    var sessions = await service.GetAllSessionsAsync();
    return Results.Ok(sessions);
});

app.Run();
```

---

## Logging Strategy

```csharp
// Use structured logging with Serilog
public class TrainingService
{
    private readonly ILogger<TrainingService> _logger;
    
    public async Task<TrainingSession> StartTrainingAsync(TrainingRequest request)
    {
        using var activity = Activity.StartActivity("StartTraining");
        
        _logger.LogInformation(
            "Starting training session {RunId} with config {ConfigPath}",
            request.RunId,
            request.ConfigPath);
        
        try
        {
            // ... training logic ...
            
            _logger.LogInformation(
                "Training session {RunId} started successfully with ID {SessionId}",
                request.RunId,
                session.Id);
                
            return session;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to start training session {RunId}",
                request.RunId);
            throw;
        }
    }
}
```

---

## Testing Approach

```csharp
public class TrainingServiceTests
{
    [Fact]
    public async Task StartTraining_WithValidConfig_ReturnsSuccess()
    {
        // Arrange
        var service = new TrainingService(
            Mock.Of<ITrainingRepository>(),
            Mock.Of<ITrainingProcessManager>());
            
        var request = new TrainingRequest(
            ConfigPath: "test.yaml",
            RunId: "test-run");
        
        // Act
        var result = await service.StartTrainingAsync(request);
        
        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value.RunId.Should().Be("test-run");
    }
    
    [Fact]
    public async Task StartTraining_WithMissingConfig_ReturnsFailure()
    {
        // Arrange
        var service = new TrainingService(
            Mock.Of<ITrainingRepository>(),
            Mock.Of<ITrainingProcessManager>());
            
        var request = new TrainingRequest(
            ConfigPath: "missing.yaml",
            RunId: "test-run");
        
        // Act
        var result = await service.StartTrainingAsync(request);
        
        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("not found");
    }
}
```

---

## Configuration Files

### appsettings.json
```json
{
  "Serilog": {
    "MinimumLevel": {
      "Default": "Information"
    },
    "WriteTo": [
      {
        "Name": "Console"
      },
      {
        "Name": "File",
        "Args": {
          "path": "logs/mentor-.txt",
          "rollingInterval": "Day"
        }
      }
    ]
  },
  "MLAgents": {
    "PythonPath": "python",
    "DefaultTimeout": 3600,
    "ResultsDirectory": "C:/MLAgents/results",
    "MaxConcurrentSessions": 3
  },
  "Database": {
    "ConnectionString": "Filename=mentor.db;Mode=Exclusive"
  }
}
```

---

## Development Workflow

### 1. Initial Setup
```bash
# Create solution
dotnet new sln -n MENTOR
dotnet new classlib -n MENTOR.Core -o src/MENTOR.Core
dotnet new web -n MENTOR.API -o src/MENTOR.API
dotnet new xunit -n MENTOR.Core.Tests -o tests/MENTOR.Core.Tests

# Add projects to solution
dotnet sln add src/MENTOR.Core
dotnet sln add src/MENTOR.API
dotnet sln add tests/MENTOR.Core.Tests

# Add references
dotnet add src/MENTOR.API reference src/MENTOR.Core
dotnet add tests/MENTOR.Core.Tests reference src/MENTOR.Core
```

### 2. Running Locally
```bash
# From MENTOR.API directory
dotnet run

# API will be available at http://localhost:5000
```

### 3. Testing
```bash
# Run all tests
dotnet test

# With coverage
dotnet test /p:CollectCoverage=true
```

---

## Best Practices Checklist

✅ **Every public method has XML documentation**
```csharp
/// <summary>
/// Starts a new ML-Agents training session.
/// </summary>
/// <param name="request">Training configuration parameters</param>
/// <returns>Created training session or error</returns>
```

✅ **Use nullable reference types**
```csharp
#nullable enable
public string? OptionalField { get; set; }
```

✅ **Async all the way**
```csharp
// Use async/await for I/O operations
public async Task<Result> ProcessAsync() { }
```

✅ **Guard clauses at method start**
```csharp
public void ProcessData(string data)
{
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length == 0) throw new ArgumentException("Data cannot be empty");
    
    // Main logic here
}
```

✅ **One class per file**

✅ **Dependency injection everywhere**

✅ **No static classes for stateful operations**

---

## Next Steps

1. **Set up the project structure** using the commands above
2. **Implement core models** (TrainingSession, TrainingRequest, etc.)
3. **Create process manager** for ML-Agents interaction
4. **Build API endpoints** one at a time
5. **Add tests** as you go
6. **Iterate and refine**

