# MENTOR - Development Guide
**ML-Ecosystem Navigation for Training Of Reinforcement Agents**

A Go service that automates ML-Agents training workflows on Windows.

---

## Table of Contents
1. [Problem & Goals](#problem--goals)
2. [Environment & Constraints](#environment--constraints)
3. [Core Requirements](#core-requirements)
4. [Go Development Principles](#go-development-principles)
5. [Project Structure](#project-structure)
6. [Implementation Guidelines](#implementation-guidelines)
7. [Technical Decisions](#technical-decisions)
8. [Getting Started](#getting-started)

---

## Problem & Goals

### The Problem
Unity ML-Agents training is a manual, repetitive process:
- Manually run commands with different parameters
- Watch terminal output to monitor progress
- Parse TensorBoard logs to understand results
- Compare multiple training runs manually
- Keep track of which configurations were used

### Project Goal
Automate ML-Agents training workflows:
1. Start training sessions with custom parameters
2. Monitor multiple training runs
3. Analyze results automatically
4. Generate human-readable reports

---

## Environment & Constraints

### Environment
- **OS**: Windows 10/11 local machine
- **Language**: Go 1.21+
- **ML-Agents**: Python package (mlagents) already installed
- **Command**: `mlagents-learn` available in PATH
- **Architecture**: Go service calls mlagents-learn as subprocess

### ML-Agents Training Basics

**Editor Mode** (default):
```bash
mlagents-learn config.yaml --run-id=MyRun
# Waits for user to click Play in Unity Editor
```

**Executable Mode** (automated):
```bash
mlagents-learn config.yaml --run-id=MyRun --env=path/to/build.exe
# Runs automatically with built Unity executable
```

**Key Outputs:**
- TensorBoard logs: `results/<run-id>/<behavior_name>/events.out.tfevents.*`
- Trained model: `results/<run-id>/<behavior_name>.onnx`
- Training logs: stdout from mlagents-learn process

### Scope

**What the Service Should NOT Do:**
- ❌ Install or manage ML-Agents Python package
- ❌ Build Unity executables
- ❌ Create Unity environments
- ❌ Modify ML-Agents source code

**What the Service SHOULD Do:**
- ✅ Orchestrate mlagents-learn processes (via Go subprocess management)
- ✅ Support both Editor and Executable modes
- ✅ Track training sessions and metadata
- ✅ Parse and analyze results
- ✅ Generate reports

---

## Core Requirements

### 1. Training Session Management

**Must be able to:**
- Start a training session with configurable parameters
- Support both Unity Editor mode (default) and Executable mode (optional path)
- Run training sessions sequentially (one at a time for MVP)
- Stop/cancel running sessions
- Track session state (pending, running, completed, failed)

**Note for MVP:** Sessions run one at a time. Parallel execution and resume functionality deferred to post-MVP.

**Configurable Parameters:**
- ML-Agents config file path (YAML)
- Unique run identifier
- Unity environment path (optional - determines mode)
- Training parameters (max_steps, learning_rate, etc.)
- Environment arguments (--no-graphics, --width, --height)
- Resume/force flags

### 2. Real-time Monitoring

**Must be able to:**
- Monitor training progress from mlagents-learn output
- Extract key metrics from logs (step, mean reward, std)
- Detect when training completes
- Detect when .onnx model file is created
- Report current status to users

### 3. Results Analysis

**Must be able to:**
- Parse TensorBoard event files (using Go protobuf)
- Extract training metrics over time
- Calculate summary statistics (final reward, convergence, duration)
- Compare multiple training runs
- Identify best-performing configurations

### 4. Report Generation

**Must be able to:**
- Generate human-readable training reports (HTML/PDF)
- Include visualizations (reward curves, statistics)
- Show configuration used
- Link to trained model files
- Support comparison reports for multiple runs

### 5. Service API

**Must provide:**
- REST API for external control (using Go standard library or popular frameworks)
- Endpoints for starting/stopping training
- Status and metrics queries
- Session listing and management
- Report generation requests

### 6. Data Persistence

**Must track:**
- Training session metadata (config, start/end times, status)
- Session-to-model file mappings
- Historical metrics
- Simple local storage using JSON files (MVP) or embedded database (SQLite, BoltDB)

---

## Go Development Principles

### Core Idioms
- **Errors are values** - Return errors, don't panic
- **Accept interfaces, return structs** - Make code flexible and testable
- **Clear is better than clever** - Write obvious code
- **Composition over inheritance** - Embed structs when needed
- **Simplicity** - Keep it simple, avoid over-engineering

### Code Quality
- **Simplicity**: One function does one thing
- **Clarity**: Descriptive names, clear intent
- **Maintainability**: Easy to understand in 6 months
- **Testability**: Components can be tested independently
- **Go idioms**: Follow standard Go conventions and patterns

### Code Clarity
- **Descriptive names** - `StartTrainingSession()` not `Run()`
- **Standard naming** - Follow Go conventions (MixedCaps for exports)
- **Comment exported items** - Every exported type, function, constant needs a doc comment
- **Package comments** - Each package needs a doc.go file
- **Keep functions short** - If it doesn't fit on one screen, break it up

### Architecture
- **Modularity**: Separate concerns (training, analysis, API)
- **Explicit**: No hidden dependencies or magic
- **Robust**: Handle failures gracefully with proper error handling
- **Observable**: Log everything important using structured logging

### Configuration
- **Flexible**: Support different training scenarios
- **Validated**: Catch errors before starting long processes
- **Documented**: Clear examples and defaults
- **Versionable**: Track what configuration was used

### What to Avoid

❌ **Panic for normal errors** - Use error returns  
❌ **Ignoring errors** - Always handle returned errors  
❌ **Global state** - Pass dependencies explicitly  
❌ **init() for complex logic** - Keep init() minimal  
❌ **goroutine leaks** - Always have a way to stop goroutines  
❌ **Premature optimization** - Measure first  
❌ **Interface pollution** - Don't define interfaces you don't need  
❌ **Deep nesting** - Early returns keep code flat  

---

## Project Structure

### Standard Go Layout

```
mentor/
├── cmd/
│   └── mentor/              # Main application entry point
│       └── main.go
├── internal/                # Private application code
│   ├── training/           # Training session management
│   │   ├── session.go
│   │   ├── manager.go
│   │   └── monitor.go
│   ├── analysis/           # Results analysis
│   │   ├── parser.go
│   │   ├── metrics.go
│   │   └── report.go
│   ├── api/                # HTTP API handlers
│   │   ├── server.go
│   │   └── handlers.go
│   ├── storage/            # Data persistence
│   │   └── repository.go
│   └── config/             # Configuration handling
│       └── config.go
├── pkg/                     # Public libraries (if needed)
├── web/                     # Web assets (templates, static files)
│   └── templates/
├── configs/                 # Configuration file examples
├── scripts/                 # Build and utility scripts
├── test/                    # Additional test data
├── go.mod
├── go.sum
├── README.md
└── Makefile
```

---

## Implementation Guidelines

### 1. Error Handling (The Go Way)

```go
// Good - Explicit error handling with context
func StartTrainingSession(configPath string) (*TrainingSession, error) {
    config, err := loadConfig(configPath)
    if err != nil {
        return nil, fmt.Errorf("failed to load config: %w", err)
    }
    
    session, err := createSession(config)
    if err != nil {
        return nil, fmt.Errorf("failed to create session: %w", err)
    }
    
    return session, nil
}

// Bad - Ignoring errors
func StartTrainingSession(configPath string) *TrainingSession {
    config, _ := loadConfig(configPath)
    session, _ := createSession(config)
    return session
}

// Also Bad - Panic for recoverable errors
func StartTrainingSession(configPath string) *TrainingSession {
    config, err := loadConfig(configPath)
    if err != nil {
        panic(err) // Don't do this!
    }
    return createSession(config)
}
```

**Principles:**
- Return errors explicitly, don't panic
- Wrap errors with context using `fmt.Errorf` with `%w`
- Define custom error types for domain-specific errors

### 2. Struct Design

```go
// Good - Clear, documented, uses standard patterns
// TrainingSession represents an active ML-Agents training session
type TrainingSession struct {
    ID              string
    ConfigPath      string
    Status          SessionStatus
    StartTime       time.Time
    EndTime         *time.Time
    Metrics         *TrainingMetrics
    cmd             *exec.Cmd      // unexported, internal detail
    outputChan      chan string    // unexported, internal detail
}

// SessionStatus represents the current state of a training session
type SessionStatus int

const (
    StatusPending SessionStatus = iota
    StatusRunning
    StatusCompleted
    StatusFailed
    StatusCancelled
)

// Bad - Unclear abbreviations and no documentation
type TS struct {
    Id  string
    Cfg string
    St  int
    T1  time.Time
    T2  *time.Time
}
```

### 3. Interface Design

```go
// Good - Small, focused interfaces
// SessionRepository defines storage operations for training sessions
type SessionRepository interface {
    Save(session *TrainingSession) error
    FindByID(id string) (*TrainingSession, error)
    FindAll() ([]*TrainingSession, error)
    Delete(id string) error
}

// MetricsParser defines how to parse training metrics
type MetricsParser interface {
    ParseTensorBoardLog(path string) (*TrainingMetrics, error)
}

// Bad - Too many responsibilities in one interface
type Repository interface {
    SaveSession(*TrainingSession) error
    FindSession(string) (*TrainingSession, error)
    SaveMetrics(*Metrics) error
    FindMetrics(string) (*Metrics, error)
    SaveConfig(*Config) error
    FindConfig(string) (*Config, error)
    GenerateReport(string) (*Report, error)
}
```

### 4. Configuration Management

```go
// Good - Use struct tags for YAML/JSON unmarshaling
type Config struct {
    Server struct {
        Host string `yaml:"host" json:"host"`
        Port int    `yaml:"port" json:"port"`
    } `yaml:"server" json:"server"`
    
    Training struct {
        ResultsDir      string `yaml:"results_dir" json:"results_dir"`
        DefaultMaxSteps int    `yaml:"default_max_steps" json:"default_max_steps"`
    } `yaml:"training" json:"training"`
}

// Load config with proper error handling
func LoadConfig(path string) (*Config, error) {
    data, err := os.ReadFile(path)
    if err != nil {
        return nil, fmt.Errorf("read config file: %w", err)
    }
    
    var config Config
    if err := yaml.Unmarshal(data, &config); err != nil {
        return nil, fmt.Errorf("parse config: %w", err)
    }
    
    if err := validateConfig(&config); err != nil {
        return nil, fmt.Errorf("validate config: %w", err)
    }
    
    return &config, nil
}
```

### 5. Subprocess Management (ML-Agents)

```go
// Good - Proper subprocess handling with context
func (m *SessionManager) startMLAgents(ctx context.Context, session *TrainingSession) error {
    cmd := exec.CommandContext(ctx, "mlagents-learn",
        session.ConfigPath,
        "--run-id="+session.ID,
    )
    
    // Set up stdout/stderr pipes for real-time monitoring
    stdout, err := cmd.StdoutPipe()
    if err != nil {
        return fmt.Errorf("create stdout pipe: %w", err)
    }
    
    stderr, err := cmd.StderrPipe()
    if err != nil {
        return fmt.Errorf("create stderr pipe: %w", err)
    }
    
    // Start command
    if err := cmd.Start(); err != nil {
        return fmt.Errorf("start mlagents-learn: %w", err)
    }
    
    // Monitor output synchronously
    // Note: For MVP, monitoring happens in main goroutine
    // Advanced: can be moved to background goroutines later
    
    // Wait for completion
    if err := cmd.Wait(); err != nil {
        return fmt.Errorf("mlagents-learn failed: %w", err)
    }
    
    return nil
}
```

**Principles:**
- Use `os/exec` package for running mlagents-learn
- Handle stdout/stderr streaming in real-time
- Implement proper signal handling for graceful shutdown
- Handle Windows-specific path separators

### 6. HTTP API Design

```go
// Good - Clear handler structure with proper error handling
type Server struct {
    manager *SessionManager
    logger  *log.Logger
    router  *http.ServeMux
}

func (s *Server) handleStartTraining(w http.ResponseWriter, r *http.Request) {
    if r.Method != http.MethodPost {
        http.Error(w, "Method not allowed", http.StatusMethodNotAllowed)
        return
    }
    
    var req StartTrainingRequest
    if err := json.NewDecoder(r.Body).Decode(&req); err != nil {
        s.respondError(w, "invalid request body", http.StatusBadRequest)
        return
    }
    
    if err := validateStartRequest(&req); err != nil {
        s.respondError(w, err.Error(), http.StatusBadRequest)
        return
    }
    
    session, err := s.manager.StartSession(r.Context(), req.toConfig())
    if err != nil {
        s.logger.Printf("Failed to start training: %v", err)
        s.respondError(w, "failed to start training", http.StatusInternalServerError)
        return
    }
    
    s.respondJSON(w, session, http.StatusCreated)
}

func (s *Server) respondJSON(w http.ResponseWriter, data interface{}, status int) {
    w.Header().Set("Content-Type", "application/json")
    w.WriteHeader(status)
    if err := json.NewEncoder(w).Encode(data); err != nil {
        s.logger.Printf("Failed to encode response: %v", err)
    }
}

func (s *Server) respondError(w http.ResponseWriter, message string, status int) {
    s.respondJSON(w, map[string]string{"error": message}, status)
}
```

### 7. Testing

```go
// Good - Table-driven tests
func TestParseTrainingOutput(t *testing.T) {
    tests := []struct {
        name    string
        input   string
        want    *TrainingMetrics
        wantErr bool
    }{
        {
            name: "valid output",
            input: "Step: 1000. Mean Reward: 0.5. Std: 0.1",
            want: &TrainingMetrics{
                Step:       1000,
                MeanReward: 0.5,
                StdReward:  0.1,
            },
            wantErr: false,
        },
        {
            name:    "invalid format",
            input:   "garbage data",
            want:    nil,
            wantErr: true,
        },
    }
    
    for _, tt := range tests {
        t.Run(tt.name, func(t *testing.T) {
            got, err := ParseTrainingOutput(tt.input)
            if (err != nil) != tt.wantErr {
                t.Errorf("ParseTrainingOutput() error = %v, wantErr %v", err, tt.wantErr)
                return
            }
            if !reflect.DeepEqual(got, tt.want) {
                t.Errorf("ParseTrainingOutput() = %v, want %v", got, tt.want)
            }
        })
    }
}

// Good - Use interfaces for mocking
type MockSessionRepository struct {
    SaveFunc    func(*TrainingSession) error
    FindByIDFunc func(string) (*TrainingSession, error)
}

func (m *MockSessionRepository) Save(s *TrainingSession) error {
    if m.SaveFunc != nil {
        return m.SaveFunc(s)
    }
    return nil
}

func (m *MockSessionRepository) FindByID(id string) (*TrainingSession, error) {
    if m.FindByIDFunc != nil {
        return m.FindByIDFunc(id)
    }
    return nil, nil
}
```

**Principles:**
- Write table-driven tests
- Use interfaces for dependency injection
- Mock external dependencies (subprocess, file system)
- Aim for high test coverage

### 8. Concurrency (Post-MVP)

**MVP Approach:**
- Sequential execution only (one session at a time)
- No goroutines for parallel sessions
- Simpler state management

**Future Considerations:**
- Use goroutines for parallel training sessions
- Use channels for communication between components
- Always implement proper context cancellation
- Use sync primitives (Mutex, WaitGroup) when adding concurrency

### 9. Windows-Specific Considerations

#### File Paths
```go
// Good - Use filepath.Join for cross-platform paths
configPath := filepath.Join(baseDir, "configs", "training.yaml")

// Bad - Hardcoded separators
configPath := baseDir + "\\configs\\training.yaml"
```

#### Process Management
```go
// Good - Handle Windows process termination
import (
    "os/exec"
    "syscall"
)

func (m *SessionManager) stopProcess(cmd *exec.Cmd) error {
    if runtime.GOOS == "windows" {
        // Windows doesn't support SIGTERM
        return cmd.Process.Signal(os.Kill)
    }
    return cmd.Process.Signal(syscall.SIGTERM)
}
```

---

## Technical Decisions

### Dependencies

**Consider These Libraries:**
- **HTTP Router**: `gorilla/mux` or `chi` (or just `net/http` for simple cases)
- **Configuration**: `spf13/viper` or just `encoding/json`/`gopkg.in/yaml.v3`
- **Logging**: `log/slog` (standard library Go 1.21+) or `sirupsen/logrus`
- **Database**: `mattn/go-sqlite3` or `etcd-io/bbolt` for embedded storage (post-MVP)
- **CLI**: `spf13/cobra` if building CLI commands
- **Testing**: `stretchr/testify` for assertions (optional, nice to have)

**Principles:**
1. **Standard library first** - Use built-in packages when possible
2. **Minimal dependencies** - Each dependency is a maintenance burden
3. **Well-maintained only** - Check last commit date and issues
4. **Go modules** - Always use go.mod for versioning

### Architecture Decisions

You should propose and implement:

1. **Project structure** - Standard Go project layout (`cmd/`, `internal/`, `pkg/`)
2. **Tech stack** - Which Go libraries for API, storage, parsing?
3. **Data storage** - Simple JSON files for MVP, SQLite/BoltDB for future
4. **Configuration format** - YAML for training parameters
5. **API design** - REST endpoints and request/response formats
6. **Monitoring approach** - How to capture and parse subprocess output?
7. **Report format** - HTML templates using Go templating?

**MVP Note:** No concurrency model needed - sessions run sequentially.

### Documentation Requirements

**Required Documentation:**
- **README.md** - Project overview, installation, quick start, API examples
- **doc.go** for each package - Package-level documentation
- **Godoc comments** - Every exported symbol needs a comment
- **API.md** - Endpoint documentation with curl examples
- **CONTRIBUTING.md** - How to build, test, contribute

**Godoc Style:**
```go
// Package training provides functionality for managing ML-Agents training sessions.
//
// This package handles the lifecycle of training sessions including starting,
// monitoring, and stopping mlagents-learn processes.
package training

// SessionManager coordinates training sessions.
//
// For MVP, it handles one session at a time sequentially.
type SessionManager struct {
    // ...
}

// StartSession begins a new training session with the given configuration.
//
// It returns an error if:
//   - The configuration is invalid
//   - A session is already running
//   - The mlagents-learn command fails to start
func (m *SessionManager) StartSession(ctx context.Context, config *Config) error {
    // ...
}
```

---

## Getting Started

### Success Criteria

**MVP (Minimum Viable Product):**
1. Start a training session via API (one at a time)
2. Monitor progress in real-time
3. Detect completion
4. Parse results from TensorBoard logs
5. Generate basic HTML report

**Post-MVP Features (Deferred):**
- Parallel training runs using goroutines
- Resume interrupted sessions
- Advanced comparison tools
- Automated hyperparameter sweeps
- Integration with experiment tracking (MLflow, W&B)
- Webhook notifications on completion

### Design Considerations

As you design this, consider:
- How will you handle long-running subprocess (training can take hours)?
- How will users specify Unity environment mode (Editor vs Executable)?
- What happens if training crashes?
- How will you parse different versions of mlagents-learn output?
- What's the simplest way to generate useful reports using Go templates?
- How will you handle Windows-specific subprocess management?

**MVP Simplifications:**
- Only one training session at a time (no concurrency)
- No resume functionality (restart from scratch if interrupted)

### Starting Point

Begin by designing:
1. Core data models (structs for "training session")
2. The workflow (what happens when user starts training?)
3. Standard Go project structure
4. A simple working prototype

**Show your architectural proposal before implementing. Explain your reasoning for key decisions.**

### Build and Run

**Makefile Example:**
```makefile
.PHONY: build test run clean

build:
	go build -o bin/mentor cmd/mentor/main.go

test:
	go test -v -race -coverprofile=coverage.out ./...

run:
	go run cmd/mentor/main.go

clean:
	rm -rf bin/
	go clean -testcache

install:
	go install ./cmd/mentor

lint:
	golangci-lint run

fmt:
	go fmt ./...
	goimports -w .
```

### Setup Checklist

- [ ] Initialize Go module: `go mod init github.com/yourusername/mentor`
- [ ] Set up standard project structure (cmd/, internal/, pkg/)
- [ ] Create configuration structs and YAML examples
- [ ] Implement basic subprocess execution of mlagents-learn
- [ ] Add HTTP server with health check endpoint
- [ ] Write tests for core functionality
- [ ] Set up Makefile for common tasks
- [ ] Document API endpoints
- [ ] Add README with quick start guide

---

## Decision Framework

**When Making Decisions, Ask Yourself:**

1. Is this idiomatic Go code?
2. Am I handling errors properly?
3. Can this goroutine leak?
4. Would a new Go developer understand this?
5. Is this the simplest solution?
6. Am I following standard Go project layout?

**If the answer is no to any of these, reconsider your approach.**

---

**Start simple, iterate, and always prefer clarity over cleverness.**
