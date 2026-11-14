"""Emulator launching through QProcess.

The executable and each argument are passed separately (no shell, no string
splitting), so paths containing spaces are safe. The same ``LaunchController``
serves both Play and Test Launch.
"""
from __future__ import annotations

import os

from PySide6.QtCore import QObject, QProcess, Signal

from .db import EmulatorProfile

GAME_PLACEHOLDER = "{game}"

# Eden (a Switch 1 emulator) accepts "-g <path>" to boot a game file directly.
# Verified on Eden v0.2.1 for Windows (2026-11-13). Re-check with Test Launch on
# other builds before labeling them supported.
EDEN_PRESET_ARGS = ["-g", GAME_PLACEHOLDER]


def build_arguments(args: list[str], game_path: str) -> list[str]:
    return [a.replace(GAME_PLACEHOLDER, game_path) for a in args]


def effective_working_dir(profile: EmulatorProfile) -> str:
    return profile.working_dir.strip() or os.path.dirname(profile.executable)


def validate_launch(profile: EmulatorProfile | None, game_path: str | None) -> list[str]:
    """Return human-readable problems that would prevent a launch."""
    problems: list[str] = []
    if profile is None:
        return ["No emulator is set up yet. Open Emulator Settings and choose your Eden executable."]
    exe = profile.executable.strip()
    if not exe:
        problems.append("The emulator profile has no executable selected.")
    elif not os.path.isfile(exe):
        problems.append(f"Emulator executable not found:\n{exe}")
    if profile.working_dir.strip() and not os.path.isdir(profile.working_dir):
        problems.append(f"Working directory not found:\n{profile.working_dir}")
    if not game_path:
        problems.append("This library entry has no game file.")
    elif not os.path.isfile(game_path):
        problems.append(f"Game file not found:\n{game_path}")
    if not any(GAME_PLACEHOLDER in a for a in profile.args):
        problems.append(
            f"The argument list doesn't contain {GAME_PLACEHOLDER}, so the game path would not be passed."
        )
    return problems


def describe_command(profile: EmulatorProfile, game_path: str) -> str:
    """Display-only rendering of the command (Windows-style quoting)."""
    parts = [profile.executable or "<executable>"] + build_arguments(profile.args, game_path)
    return " ".join(f'"{p}"' if (" " in p or not p) else p for p in parts)


class LaunchController(QObject):
    launched = Signal(str, int)  # tag, pid
    finished = Signal(str, int, bool)  # tag, exit code, crashed
    failed = Signal(str, str)  # tag, message

    def __init__(self, log_path: str | None = None, parent: QObject | None = None):
        super().__init__(parent)
        self._process: QProcess | None = None
        self._tag = ""
        self._log_path = log_path
        self._started = False

    @property
    def current_tag(self) -> str:
        return self._tag

    def is_running(self) -> bool:
        return self._process is not None

    def launch(self, profile: EmulatorProfile, game_path: str, tag: str = "") -> bool:
        """Start the emulator. Returns False (and emits ``failed``) on validation errors."""
        if self.is_running():
            self.failed.emit(tag, "An emulator is already running. Close it before launching another game.")
            return False
        problems = validate_launch(profile, game_path)
        if problems:
            self.failed.emit(tag, "\n\n".join(problems))
            return False

        proc = QProcess(self)
        proc.setProgram(profile.executable)
        proc.setArguments(build_arguments(profile.args, game_path))
        proc.setWorkingDirectory(effective_working_dir(profile))
        if self._log_path:
            proc.setProcessChannelMode(QProcess.ProcessChannelMode.MergedChannels)
            proc.setStandardOutputFile(self._log_path)
        else:
            proc.setStandardOutputFile(QProcess.nullDevice())
            proc.setStandardErrorFile(QProcess.nullDevice())
        proc.started.connect(self._on_started)
        proc.errorOccurred.connect(self._on_error)
        proc.finished.connect(self._on_finished)

        self._process = proc
        self._tag = tag
        self._started = False
        proc.start()
        return True

    def stop(self) -> None:
        if self._process is not None:
            self._process.kill()
            self._process.waitForFinished(3000)

    # ----------------------------------------------------------------- internal
    def _on_started(self) -> None:
        self._started = True
        pid = int(self._process.processId()) if self._process else 0
        self.launched.emit(self._tag, pid)

    def _on_error(self, error: QProcess.ProcessError) -> None:
        if error == QProcess.ProcessError.FailedToStart and not self._started:
            proc = self._process
            message = (
                "The emulator could not be started.\n\n"
                f"{proc.errorString() if proc else ''}\n\n"
                "Check that the executable is a valid Windows program and that you have permission to run it."
            )
            tag = self._tag
            self._cleanup()
            self.failed.emit(tag, message)

    def _on_finished(self, exit_code: int, exit_status: QProcess.ExitStatus) -> None:
        tag = self._tag
        crashed = exit_status == QProcess.ExitStatus.CrashExit
        self._cleanup()
        self.finished.emit(tag, int(exit_code), crashed)

    def _cleanup(self) -> None:
        if self._process is not None:
            self._process.deleteLater()
        self._process = None
        self._started = False
