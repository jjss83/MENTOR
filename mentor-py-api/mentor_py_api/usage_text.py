"""Mirrors the CLI usage text presented by the .NET mentor-api."""


def get_training_usage() -> str:
    return (
        "Usage:\n"
        "  python -m mentor_py_api --env-path <path-to-env-exe> --config <trainer-config.yaml> [options]\n\n"
        "Options:\n"
        "  --run-id <id>             Optional run identifier. Default: run-<behavior>-<UTC timestamp>\n"
        "  --results-dir <path>      Directory to store training artifacts. Default: X:\workspace\ml-agents\results\n"
        "  --conda-env <name>        Name of the ML-Agents Conda environment. Default: mlagents\n"
        "  --base-port <port>        Base port to use when launching the environment\n"
        "  --no-graphics             Launches the environment without rendering\n"
        "  --skip-conda              Assume the ML-Agents tooling is already on PATH\n"
        "  --tensorboard             Also start TensorBoard pointed at the results directory\n\n"
        "Report usage:\n"
        "  python -m mentor_py_api report --run-id <id> [--results-dir <path>]\n\n"
        "Report interpreter usage:\n"
        "  python -m mentor_py_api report-interpreter --run-id <id> [--results-dir <path>] [--prompt \"Explain current results\"]"
        " [--openai-model <model>] [--openai-api-key <key>] [--check-openai]"
    )


def get_report_usage() -> str:
    return (
        "Usage:\n"
        "  python -m mentor_py_api report --run-id <id> [--results-dir <path>]\n\n"
        "Options:\n"
        "  --run-id <id>        Run identifier to inspect (required)\n"
        "  --results-dir <path> Directory that contains run artifacts. Default: X:\workspace\ml-agents\results"
    )


def get_report_interpreter_usage() -> str:
    return (
        "Usage:\n"
        "  python -m mentor_py_api report-interpreter --run-id <id> [--results-dir <path>] [--prompt <text>] [--openai-model <model>]"
        " [--openai-api-key <key>] [--check-openai]\n\n"
        "Options:\n"
        "  --run-id <id>        Run identifier to inspect (required)\n"
        "  --results-dir <path> Directory that contains run artifacts. Default: X:\workspace\ml-agents\results\n"
        "  --prompt <text>      Prompt to send along with the report. Default: Explain current results\n"
        "  --openai-model <m>   OpenAI chat completion model. Default: gpt-4o-mini\n"
        "  --openai-api-key <k> Explicit API key (otherwise uses OPENAI_API_KEY env var)\n"
        "  --check-openai       Skip report generation and issue a simple connectivity check call"
    )
