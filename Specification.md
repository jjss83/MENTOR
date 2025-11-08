# MENTOR - ML-Agents Training Automation Service

## Problem Statement
Unity ML-Agents training is a manual, repetitive process. Developers must:
- Manually run commands with different parameters
- Watch terminal output to monitor progress
- Parse TensorBoard logs to understand results
- Compare multiple training runs manually
- Keep track of which configurations were used

We need a service that automates these workflows.

## Project Goal
Build a service that automates ML-Agents training workflows on Windows, making it easy to:
1. Start training sessions with custom parameters
2. Monitor multiple training runs
3. Analyze results automatically
4. Generate human-readable reports

## Constraints & Assumptions

### Environment
- Windows 10/11 local machine
- Python 3.10.x environment
- ML-Agents Python package (mlagents) is **already installed** in the environment
- `mlagents-learn` command is available in PATH

### ML-Agents Training Basics
The service wraps the `mlagents-learn` command which has two modes:

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

### What the Service Should NOT Do
- ❌ Install or manage ML-Agents Python package
- ❌ Build Unity executables
- ❌ Create Unity environments
- ❌ Modify ML-Agents source code

### What the Service SHOULD Do
- ✅ Orchestrate mlagents-learn processes
- ✅ Support both Editor and Executable modes
- ✅ Track training sessions and metadata
- ✅ Parse and analyze results
- ✅ Generate reports

## Core Requirements

### 1. Training Session Management
**Must be able to:**
- Start a training session with configurable parameters
- Support both Unity Editor mode (default) and Executable mode (optional path)
- Run multiple training sessions (sequentially or in parallel)
- Stop/cancel running sessions
- Resume interrupted sessions
- Track session state (pending, running, completed, failed)

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
- Parse TensorBoard event files
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
- REST API for external control
- Endpoints for starting/stopping training
- Status and metrics queries
- Session listing and management
- Report generation requests

### 6. Data Persistence
**Must track:**
- Training session metadata (config, start/end times, status)
- Session-to-model file mappings
- Historical metrics
- Simple local storage (no cloud dependencies)

## Development Principles

### Code Quality
- **Simplicity**: One function does one thing
- **Clarity**: Descriptive names, clear intent
- **Maintainability**: Easy to understand in 6 months
- **Testability**: Components can be tested independently

### Architecture
- **Modularity**: Separate concerns (training, analysis, API)
- **Explicit**: No hidden dependencies or magic
- **Robust**: Handle failures gracefully
- **Observable**: Log everything important

### Configuration
- **Flexible**: Support different training scenarios
- **Validated**: Catch errors before starting long processes
- **Documented**: Clear examples and defaults
- **Versionable**: Track what configuration was used

## Success Criteria

### MVP (Minimum Viable Product)
1. Start a training session via API
2. Monitor progress in real-time
3. Detect completion
4. Parse results from TensorBoard logs
5. Generate basic HTML report

### Nice to Have
- Parallel training runs
- Advanced comparison tools
- Automated hyperparameter sweeps
- Integration with experiment tracking (MLflow, W&B)
- Email/webhook notifications on completion

## Technical Decisions You Should Make

Please propose and implement:
1. **Project structure** - How should code be organized?
2. **Tech stack** - Which Python libraries for API, task queue, parsing?
3. **Data storage** - How to store session metadata?
4. **Configuration format** - How should users specify training parameters?
5. **API design** - What endpoints and request/response formats?
6. **Monitoring approach** - How to capture and parse mlagents-learn output?
7. **Report format** - What should reports include and look like?

## Questions to Consider

As you design this:
- How will you handle long-running processes (training can take hours)?
- How will you support multiple concurrent training sessions?
- How will users specify Unity environment mode (Editor vs Executable)?
- What happens if training crashes or is interrupted?
- How will you parse different versions of mlagents-learn output?
- What's the simplest way to generate useful reports?

## Starting Point

Begin by designing:
1. Core data models (what is a "training session"?)
2. The workflow (what happens when user starts training?)
3. Initial project structure
4. A simple working prototype

Show me your architectural proposal before implementing. Explain your reasoning for key decisions.