# MENTOR

MENTOR (ML-Ecosystem Navigation for Training Of Reinforcement agents) is a .NET 9 solution that orchestrates Unity ML-Agents training from a Minimal API service. The solution is split into three projects:

- `MENTOR.Core` – domain models, interfaces, and training orchestration services
- `MENTOR.API` – ASP.NET Core Minimal API surface that exposes training endpoints
- `MENTOR.Infrastructure` – LiteDB persistence, ML-Agents process management, and filesystem helpers

## Prerequisites

- [.NET SDK 9.0](https://dotnet.microsoft.com/download)
- (Optional) Unity ML-Agents CLI and Python environment if you intend to execute real training runs
- PowerShell 7 or a POSIX-compatible shell for running the provided commands

Verify your SDK installation:

```pwsh
dotnet --info
```

## Getting Started

Clone the repository and restore NuGet packages once per environment:

```pwsh
git clone https://github.com/jjss83/MENTOR.git
cd MENTOR
dotnet restore
```

Build the full solution to ensure all projects compile:

```pwsh
dotnet build MENTOR.sln
```

> The repository targets `net9.0`, so make sure your `dotnet` CLI reports version 9.0.x or higher.

## Running the API Locally

Run the Minimal API from the solution root:

```pwsh
dotnet run --project src/MENTOR.API
```

The service listens on `http://localhost:5000` by default (or the port configured in `Properties/launchSettings.json`). Use `dotnet watch run --project src/MENTOR.API` during development for hot reload.

### Configuration

Key configuration lives in the following files:

- `src/MENTOR.API/appsettings.json` – default logging and ML-Agents options
- `src/MENTOR.API/appsettings.Development.json` – local overrides

Settings can be overridden through environment variables using the standard ASP.NET Core conventions (for example, `MLAgents__PythonPath`).

#### Example: Local ML-Agents Setup

If your ML-Agents workspace is located at `X:/workspace/ml-agents` with a virtual environment named `mlagents`, add the following override to `src/MENTOR.API/appsettings.Development.json`:

```json
{
  "MLAgents": {
    "PythonPath": "X:/workspace/ml-agents/mlagents/Scripts/python.exe",
    "ResultsDirectory": "X:/workspace/ml-agents/results",
    "AdditionalArguments": []
  }
}
```

Adjust the `PythonPath` if your `python.exe` resides elsewhere (e.g., a Conda environment). The settings file merges with the base `appsettings.json`, so you only need to override values that differ locally.

## Running Tests

Execute all unit tests from the repository root:

```pwsh
dotnet test
```

To capture coverage data:

```pwsh
dotnet test /p:CollectCoverage=true
```

Project-specific test suites live in the `tests/` folder and mirror the production projects.

## Project Structure

```text
MENTOR/
├── src/
│   ├── MENTOR.Core/
│   ├── MENTOR.API/
│   └── MENTOR.Infrastructure/
├── tests/
│   ├── MENTOR.Core.Tests/
│   └── MENTOR.API.Tests/
├── docs/
├── examples/
└── MENTOR.sln
```

Additional development guidance is available in `DEVELOPMENT_GUIDE.md` and `AGENTS.md`.

## Troubleshooting

- **SDK mismatch** – ensure `dotnet --info` reports .NET 9; older SDKs cannot build the solution.
- **Missing ML-Agents CLI** – you can run the API without ML-Agents installed, but training start requests will fail at runtime until the CLI is available on the host.
- **Port conflicts** – set `ASPNETCORE_URLS` or update `launchSettings.json` to change the local HTTP port.

If you encounter build or runtime issues, run `dotnet restore` and `dotnet build` again to regenerate generated files, then consult the logs emitted by Serilog in the console.
