from __future__ import annotations

import asyncio
import os
import shlex
import socket
import subprocess
import tempfile
import threading
import uuid
from pathlib import Path
from typing import Dict, List, Optional

from .options import TrainingOptions

DEFAULT_TENSORBOARD_PORT = 6006


class TrainingSessionRunner:
    def __init__(self, options: TrainingOptions, log_path: str, tensorboard_port: Optional[int] = None) -> None:
        self.options = options
        self.log_path = log_path
        self._reuse_existing_tensorboard = False
        self._tensorboard_port = None
        if self.options.launch_tensorboard:
            self._tensorboard_port = self._determine_tensorboard_port(tensorboard_port)
        elif tensorboard_port is not None:
            self._tensorboard_port = tensorboard_port

    @property
    def tensorboard_url(self) -> Optional[str]:
        if self._tensorboard_port is None:
            return None
        return f"http://localhost:{self._tensorboard_port}"

    async def run(self) -> int:
        loop = asyncio.get_running_loop()
        return await loop.run_in_executor(None, self._run_sync)

    def _run_sync(self) -> int:
        os.makedirs(self.options.results_directory, exist_ok=True)
        temp_env_dir = self._create_isolated_temp_dir()
        env = os.environ.copy()
        env.update({"TMP": str(temp_env_dir), "TEMP": str(temp_env_dir), "TMPDIR": str(temp_env_dir)})

        training_command = self._build_training_command()
        tensorboard_command = self._build_tensorboard_command() if self._should_launch_tensorboard() else None

        with open(self.log_path, "a", encoding="utf-8", buffering=1) as log_file:
            def log_line(message: str = "") -> None:
                log_file.write(message + "\n")
                log_file.flush()

            log_line(f"Writing training artifacts to '{self.options.results_directory}'.")
            log_line()
            log_line("Starting training session with command:")
            log_line(self._format_command(training_command))
            log_line()

            tensorboard_process: Optional[subprocess.Popen] = None
            tensorboard_threads: List[threading.Thread] = []
            if tensorboard_command:
                log_line("Starting TensorBoard in parallel with command:")
                log_line(self._format_command(tensorboard_command))
                log_line()
                tensorboard_process = self._start_process(tensorboard_command, env=env)
                tensorboard_threads = self._pump_process_streams(tensorboard_process, log_file)
            elif self.options.launch_tensorboard and self._reuse_existing_tensorboard and self._tensorboard_port is not None:
                log_line(f"TensorBoard already running on port {self._tensorboard_port}; not launching another instance.")
                log_line()

            process = self._start_process(training_command, env=env)
            training_threads = self._pump_process_streams(process, log_file)

            exit_code = self._wait_for_process(process)
            self._join_threads(training_threads)

            if tensorboard_process and tensorboard_process.poll() is None:
                log_line()
                log_line("Training session finished. Stopping TensorBoard...")
                self._terminate_process(tensorboard_process)
            if tensorboard_process:
                tensorboard_process.wait()
                self._join_threads(tensorboard_threads)

            return exit_code

    def _build_training_command(self) -> List[str]:
        args = [self._resolve_executable()]
        if not self.options.skip_conda:
            args.extend(["run", "-n", self.options.conda_environment_name, "mlagents-learn"])
        else:
            args[0] = "mlagents-learn"

        args.extend(self._mlagents_arguments())
        return args

    def _build_tensorboard_command(self) -> Optional[List[str]]:
        if self._tensorboard_port is None:
            return None

        if self.options.skip_conda:
            args = ["tensorboard", "--logdir", self.options.results_directory, "--host", "localhost"]
        else:
            args = [
                self._resolve_executable(),
                "run",
                "-n",
                self.options.conda_environment_name,
                "tensorboard",
                "--logdir",
                self.options.results_directory,
                "--host",
                "localhost",
            ]

        if self._tensorboard_port is not None:
            args.extend(["--port", str(self._tensorboard_port)])

        return args

    def _should_launch_tensorboard(self) -> bool:
        return self.options.launch_tensorboard and not self._reuse_existing_tensorboard and self._tensorboard_port is not None

    def _mlagents_arguments(self) -> List[str]:
        args = [self.options.trainer_config_path, f"--run-id={self.options.run_id}", f"--results-dir={self.options.results_directory}", "--force"]
        if self.options.env_executable_path:
            args.append(f"--env={self.options.env_executable_path}")
        if self.options.base_port is not None:
            args.append(f"--base-port={self.options.base_port}")
        if self.options.no_graphics:
            args.append("--no-graphics")
        return args

    def _resolve_executable(self) -> str:
        if self.options.skip_conda:
            return "mlagents-learn"
        explicit = os.environ.get("CONDA_EXE")
        if explicit and os.path.isfile(explicit):
            return explicit
        return "conda"

    def _determine_tensorboard_port(self, explicit_port: Optional[int]) -> Optional[int]:
        desired = explicit_port or DEFAULT_TENSORBOARD_PORT
        if self._is_port_in_use(desired):
            self._reuse_existing_tensorboard = True
            return desired
        self._reuse_existing_tensorboard = False
        return desired

    @staticmethod
    def _is_port_in_use(port: int) -> bool:
        with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as sock:
            sock.settimeout(0.2)
            result = sock.connect_ex(("127.0.0.1", port))
            return result == 0

    @staticmethod
    def _start_process(command: List[str], env: Optional[Dict[str, str]] = None) -> subprocess.Popen:
        return subprocess.Popen(
            command,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            stdin=subprocess.DEVNULL,
            env=env,
            creationflags=subprocess.CREATE_NO_WINDOW if hasattr(subprocess, "CREATE_NO_WINDOW") else 0,
            text=False,
        )

    @staticmethod
    def _pump_process_streams(process: subprocess.Popen, log_file) -> List[threading.Thread]:
        threads: List[threading.Thread] = []
        for stream in (process.stdout, process.stderr):
            if not stream:
                continue

            def _pump(src=stream) -> None:
                for chunk in iter(lambda: src.readline(4096), b""):
                    log_file.write(chunk.decode("utf-8", errors="replace"))
                    log_file.flush()

            thread = threading.Thread(target=_pump, daemon=True)
            thread.start()
            threads.append(thread)
        return threads

    @staticmethod
    def _wait_for_process(process: subprocess.Popen) -> int:
        return process.wait()

    @staticmethod
    def _join_threads(threads: List[threading.Thread]) -> None:
        for thread in threads:
            thread.join(timeout=0.1)

    @staticmethod
    def _terminate_process(process: subprocess.Popen) -> None:
        try:
            process.terminate()
        except OSError:
            return

    @staticmethod
    def _format_command(command: List[str]) -> str:
        return " ".join(shlex.quote(part) for part in command)

    @staticmethod
    def _create_isolated_temp_dir() -> Path:
        temp_root = Path(tempfile.gettempdir()) / "mentor-cli"
        temp_root.mkdir(parents=True, exist_ok=True)
        isolated = temp_root / uuid.uuid4().hex
        isolated.mkdir(parents=True, exist_ok=True)
        return isolated
