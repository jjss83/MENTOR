# Testing Strategy for Mentor API

This document outlines how to add automated coverage for the minimal API without depending on Unity builds or long-running ML-Agents processes.

## Goals
- Verify endpoint contracts and status transitions for train/resume/stop/log flows.
- Validate option parsing and CLI argument construction independently of external tools.
- Keep tests fast and hermetic by faking process execution, filesystem access, and TensorBoard.

## Test Pyramid
- **Unit (primary):** Pure logic in `Cli/*` (option parsing, arg composition, run metadata), `TrainingRunStore` behaviors, and small helpers (e.g., log slicing).
- **Integration (targeted):** Minimal API endpoints via `WebApplicationFactory<Program>` with injected fakes for process runners and dashboard host; in-memory filesystem for run artifacts.
- **Smoke (manual):** One end-to-end check per release using a small Unity build and real ML-Agents to ensure process wiring still works.
- **Full regression (scheduled):** Periodic run of every automated suite (unit, integration, CLI parsing) plus linting and analyzers to catch regressions before releases.

## Proposed Tooling
- Framework: `xUnit` + `FluentAssertions` (or `Shouldly`) for readable assertions.
- ASP.NET host: `Microsoft.AspNetCore.Mvc.Testing` to spin up the minimal API in-memory.
- Test doubles: lightweight fakes instead of mocks where possible; optional `Moq` for behavior verification.
- Filesystem isolation: use `TempDirectory` helpers and deterministic fixture folders under `mentor-api/TestData`.

## Structure
- New project `MentorApi.Tests` beside `MentorApi.csproj` targeting `net9.0`.
- Folders:
  - `Cli/` for option and argument tests.
  - `Api/` for endpoint/integration tests.
  - `RunStore/` for state-machine and log reading tests.
  - `TestDoubles/` for fake runners, dashboard host, and process launcher abstractions.

## Seams to Add (small refactors)
- Extract interfaces around process starting and TensorBoard (`IProcessRunner`, `ITensorboardLauncher`) so tests can substitute fakes without invoking external binaries.
- Register `TrainingRunStore` and the new abstractions in DI instead of constructing directly in `Program.cs`, enabling `WebApplicationFactory` overrides.
- Move training/dashboards settings into options records bound from configuration so tests can override via `IConfiguration` or `WebApplicationFactory` settings.

## Coverage Plan
- **CliArgs / TrainingOptions**
  - Generates defaults when optional fields are omitted (config, resultsDir, runId generation).
  - Rejects invalid combinations (missing config/envPath when required, duplicate run IDs).
  - Ensures parsing preserves basePort/noGraphics/tensorboard/resume flags.
- **TrainingRunStore**
  - Allows only one active run per `runId`; returns conflict on duplicates.
  - Transitions: start → running → stopping → stopped → resumed; resume flag set/cleared on disk.
  - `ReadLog` paging respects `from` offset, handles missing files, and sets `eof` correctly.
- **Endpoints (with fakes)**
  - `/health` returns `{ status: "ok" }`.
  - `/train` returns 200 + run metadata when runner starts; 400 on invalid options; 409 when duplicate `runId`.
  - `/train/resume`, `/train/stop`, `/train/resume-flag` honor missing/invalid bodies and surface store messages.
  - `/train-status` and `/train-status/{runId}` serialize stored runs correctly with and without `resultsDir`.
  - `/train/log/{runId}` returns slices and 404 for missing logs.
- **TensorBoard**
  - Start endpoint returns existing URL when already running; respects custom `resultsDir`/`port`; errors bubble from launcher.
- **Dashboard host**
  - Startup messages reflect already-running vs. newly-started cases; failures surface message text.

## Test Data & Fixtures
- Add tiny YAML configs under `TestData/configs/` to avoid depending on real profiles.
- Use stub log files and run metadata JSON to simulate `run_logs` contents.
- Keep process outputs as simple text fixtures (no binary assets) so tests stay fast.

## Execution
- Run locally with `dotnet test` from the repo root or `mentor-api` (integration tests use the `Testing` environment and an isolated temp results directory).
- Capture coverage with Coverlet: `dotnet test /p:CollectCoverage=true /p:CoverletOutputFormat=cobertura` so CI can fail when coverage drops.
- CI hook: add a GitHub Actions job that restores, builds, and runs `dotnet test` for `MentorApi.Tests` on PRs touching `mentor-api`.

## Manual Smoke (release cadence)

- Start API with `dotnet run`, POST `/train` against a small Unity build, observe training start, stop with `/train/stop`, then resume.
- Verify `/tensorboard/start` opens on 6006 and serves logs for the run.
