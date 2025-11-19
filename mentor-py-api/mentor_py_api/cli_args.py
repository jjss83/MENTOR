from __future__ import annotations

from typing import List, Optional, Protocol


class _TrainingRequestLike(Protocol):
    results_dir: Optional[str]
    conda_env: Optional[str]
    base_port: Optional[int]
    no_graphics: Optional[bool]
    skip_conda: Optional[bool]
    tensorboard: Optional[bool]
    env_path: Optional[str]
    config: Optional[str]
    run_id: Optional[str]


class _ReportRequestLike(Protocol):
    results_dir: Optional[str]
    run_id: Optional[str]


class _ReportInterpreterRequestLike(Protocol):
    results_dir: Optional[str]
    run_id: Optional[str]
    prompt: Optional[str]
    openai_model: Optional[str]
    openai_api_key: Optional[str]
    check_openai: Optional[bool]


def from_training(
    request: _TrainingRequestLike,
    env_path_override: Optional[str] = None,
    config_override: Optional[str] = None,
    run_id_override: Optional[str] = None,
) -> List[str]:
    env_path = env_path_override or request.env_path
    config = config_override or request.config
    args: List[str] = []

    if env_path:
        args.extend(["--env-path", env_path])
    if config:
        args.extend(["--config", config])
    if run_id_override:
        args.extend(["--run-id", run_id_override])
    if request.results_dir:
        args.extend(["--results-dir", request.results_dir])
    if request.conda_env:
        args.extend(["--conda-env", request.conda_env])
    if request.base_port is not None:
        args.extend(["--base-port", str(request.base_port)])
    if request.no_graphics:
        args.append("--no-graphics")
    if request.skip_conda:
        args.append("--skip-conda")
    if request.tensorboard:
        args.append("--tensorboard")

    return args


def from_report(request: _ReportRequestLike) -> List[str]:
    args: List[str] = []
    if request.run_id:
        args.extend(["--run-id", request.run_id])
    if request.results_dir:
        args.extend(["--results-dir", request.results_dir])
    return args


def from_report_interpreter(request: _ReportInterpreterRequestLike) -> List[str]:
    args = from_report(request)
    if request.prompt:
        args.extend(["--prompt", request.prompt])
    if request.openai_model:
        args.extend(["--openai-model", request.openai_model])
    if request.openai_api_key:
        args.extend(["--openai-api-key", request.openai_api_key])
    if request.check_openai:
        args.append("--check-openai")
    return args
