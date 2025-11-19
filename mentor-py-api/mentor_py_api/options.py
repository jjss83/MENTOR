from __future__ import annotations

import os
from dataclasses import dataclass
from datetime import datetime
from typing import Optional, Sequence, Tuple

DEFAULT_RESULTS_DIRECTORY = r"X:\workspace\ml-agents\results"
DEFAULT_CONDA_ENV = "mlagents"
DEFAULT_PROMPT = "Explain current results"
DEFAULT_MODEL = "gpt-4o-mini"


def _abspath(path: str) -> str:
    try:
        return os.path.abspath(path)
    except OSError:
        return path


def _normalize_file(path: str, description: str) -> Tuple[Optional[str], Optional[str]]:
    full_path = _abspath(path)
    if not os.path.isfile(full_path):
        return None, f"Could not find the specified {description} at '{full_path}'."
    return full_path, None


def _normalize_directory(path: str) -> Tuple[Optional[str], Optional[str]]:
    try:
        return os.path.abspath(path), None
    except OSError as exc:
        return None, f"Failed to resolve directory path '{path}': {exc}"


def _default_run_id(trainer_config_path: Optional[str]) -> str:
    timestamp = datetime.utcnow().strftime("%Y%m%d-%H%M%S")
    behavior = _extract_behavior_name(trainer_config_path)
    if not behavior:
        return f"run-{timestamp}"
    return f"run-{behavior}-{timestamp}"


def _extract_behavior_name(trainer_config_path: Optional[str]) -> Optional[str]:
    if not trainer_config_path or not os.path.isfile(trainer_config_path):
        return None

    try:
        with open(trainer_config_path, "r", encoding="utf-8") as stream:
            in_behaviors = False
            behaviors_indent = 0
            for raw_line in stream:
                if not raw_line.strip():
                    continue
                indent = _count_leading_spaces(raw_line)
                trimmed = raw_line.strip()
                if trimmed.startswith("#"):
                    continue
                if not in_behaviors:
                    if trimmed.lower().startswith("behaviors:"):
                        in_behaviors = True
                        behaviors_indent = indent
                    continue
                if indent <= behaviors_indent:
                    break
                colon_index = trimmed.find(":")
                if colon_index <= 0:
                    continue
                behavior_name = trimmed[:colon_index].strip()
                normalized = _normalize_behavior_name(behavior_name)
                if normalized:
                    return normalized
    except OSError:
        return None

    return None


def _count_leading_spaces(text: str) -> int:
    count = 0
    for ch in text:
        if ch == " ":
            count += 1
        elif ch == "\t":
            count += 2
        else:
            break
    return count


def _normalize_behavior_name(behavior_name: str) -> Optional[str]:
    if not behavior_name.strip():
        return None

    chars = []
    for ch in behavior_name:
        if ch.isalnum():
            chars.append(ch.lower())
        elif ch in ("-", "_"):
            chars.append(ch)
        else:
            chars.append("-")
    normalized = "".join(chars).strip("-")
    return normalized or None


@dataclass
class TrainingOptions:
    env_executable_path: Optional[str]
    trainer_config_path: str
    run_id: str
    results_directory: str
    conda_environment_name: str
    base_port: Optional[int]
    no_graphics: bool
    skip_conda: bool
    launch_tensorboard: bool

    default_results_directory: str = DEFAULT_RESULTS_DIRECTORY

    @classmethod
    def try_parse(cls, args: Sequence[str]) -> Tuple[Optional["TrainingOptions"], Optional[str]]:
        env_path: Optional[str] = None
        config_path: Optional[str] = None
        run_id: Optional[str] = None
        results_dir: Optional[str] = None
        conda_env: Optional[str] = None
        base_port: Optional[int] = None
        no_graphics = False
        skip_conda = False
        launch_tensorboard = False

        i = 0
        args = list(args)
        while i < len(args):
            raw_arg = args[i]
            if not raw_arg.startswith("--"):
                return None, f"Unrecognized argument '{raw_arg}'."
            key = raw_arg[2:]
            if key in {"env-path", "config", "run-id", "results-dir", "conda-env", "base-port"}:
                i += 1
                if i >= len(args):
                    return None, f"Missing value for --{key}."
                value = args[i]
            else:
                value = None

            if key == "env-path":
                env_path, error = _normalize_file(value, "environment executable")
                if error:
                    return None, error
            elif key == "config":
                config_path, error = _normalize_file(value, "trainer config")
                if error:
                    return None, error
            elif key == "run-id":
                if not value.strip():
                    return None, "--run-id must not be empty."
                run_id = value.strip()
            elif key == "results-dir":
                results_dir, error = _normalize_directory(value)
                if error:
                    return None, error
            elif key == "conda-env":
                trimmed = value.strip()
                if not trimmed:
                    return None, "--conda-env must not be empty."
                conda_env = trimmed
            elif key == "base-port":
                try:
                    port = int(value)
                except ValueError:
                    return None, "--base-port must be a positive integer."
                if port <= 0:
                    return None, "--base-port must be a positive integer."
                base_port = port
            elif key == "no-graphics":
                no_graphics = True
            elif key == "skip-conda":
                skip_conda = True
            elif key == "tensorboard":
                launch_tensorboard = True
            else:
                return None, f"Unknown option '--{key}'."
            i += 1

        if not config_path:
            return None, "--config is required."

        if not results_dir:
            results_dir, error = _normalize_directory(cls.default_results_directory)
            if error:
                return None, error
        if not results_dir:
            return None, "Failed to resolve default results directory."

        if not run_id:
            run_id = _default_run_id(config_path)

        if not conda_env:
            conda_env = DEFAULT_CONDA_ENV

        options = cls(
            env_executable_path=env_path,
            trainer_config_path=config_path,
            run_id=run_id,
            results_directory=results_dir,
            conda_environment_name=conda_env,
            base_port=base_port,
            no_graphics=no_graphics,
            skip_conda=skip_conda,
            launch_tensorboard=launch_tensorboard,
        )
        return options, None


@dataclass
class ReportOptions:
    run_id: str
    results_directory: str

    @classmethod
    def try_parse(cls, args: Sequence[str]) -> Tuple[Optional["ReportOptions"], Optional[str]]:
        run_id: Optional[str] = None
        results_directory: Optional[str] = None
        args = list(args)
        i = 0
        while i < len(args):
            raw_arg = args[i]
            if not raw_arg.startswith("--"):
                return None, f"Unrecognized argument '{raw_arg}'."
            key = raw_arg[2:]
            if key in {"run-id", "results-dir"}:
                i += 1
                if i >= len(args):
                    return None, f"Missing value for --{key}."
                value = args[i]
            else:
                value = None

            if key == "run-id":
                if not value.strip():
                    return None, "--run-id must not be empty."
                run_id = value.strip()
            elif key == "results-dir":
                results_directory, error = _normalize_directory(value)
                if error:
                    return None, error
            else:
                return None, f"Unknown option '--{key}'."
            i += 1

        if not run_id:
            return None, "--run-id is required."

        if not results_directory:
            results_directory, error = _normalize_directory(DEFAULT_RESULTS_DIRECTORY)
            if error:
                return None, error
        if not results_directory:
            return None, "Failed to resolve default results directory."

        return cls(run_id=run_id, results_directory=results_directory), None


@dataclass
class ReportInterpreterOptions:
    run_id: str
    results_directory: str
    prompt: str
    openai_model: Optional[str]
    openai_api_key: Optional[str]
    check_openai: bool

    @classmethod
    def try_parse(cls, args: Sequence[str]) -> Tuple[Optional["ReportInterpreterOptions"], Optional[str]]:
        run_id: Optional[str] = None
        results_directory: Optional[str] = None
        prompt = DEFAULT_PROMPT
        openai_model: Optional[str] = None
        openai_api_key: Optional[str] = None
        check_openai = False

        args = list(args)
        i = 0
        while i < len(args):
            raw_arg = args[i]
            if not raw_arg.startswith("--"):
                return None, f"Unrecognized argument '{raw_arg}'."
            key = raw_arg[2:]
            if key in {"run-id", "results-dir", "prompt", "openai-model", "openai-api-key"}:
                i += 1
                if i >= len(args):
                    return None, f"Missing value for --{key}."
                value = args[i]
            else:
                value = None

            if key == "run-id":
                if not value.strip():
                    return None, "--run-id must not be empty."
                run_id = value.strip()
            elif key == "results-dir":
                results_directory, error = _normalize_directory(value)
                if error:
                    return None, error
            elif key == "prompt":
                if value.strip():
                    prompt = value.strip()
            elif key == "openai-model":
                openai_model = value.strip()
            elif key == "openai-api-key":
                openai_api_key = value.strip()
            elif key == "check-openai":
                check_openai = True
            else:
                return None, f"Unknown option '--{key}'."
            i += 1

        if not run_id:
            return None, "--run-id is required."

        if not results_directory:
            results_directory, error = _normalize_directory(DEFAULT_RESULTS_DIRECTORY)
            if error:
                return None, error
        if not results_directory:
            return None, "Failed to resolve default results directory."

        if not openai_model:
            openai_model = DEFAULT_MODEL

        return (
            cls(
                run_id=run_id,
                results_directory=results_directory,
                prompt=prompt,
                openai_model=openai_model,
                openai_api_key=openai_api_key,
                check_openai=check_openai,
            ),
            None,
        )
