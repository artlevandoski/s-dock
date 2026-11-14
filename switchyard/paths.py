"""Locations of Switch Yard's own user-writable data (never game files)."""
from __future__ import annotations

import os
from pathlib import Path

APP_DIR_NAME = "SwitchYard"
DATA_DIR_ENV = "SWITCHYARD_DATA_DIR"


def data_dir() -> Path:
    """Return the per-user data folder, creating it if needed.

    Defaults to %LOCALAPPDATA%\\SwitchYard. Set SWITCHYARD_DATA_DIR (or pass
    --data-dir on the command line) to use a different folder, e.g. for tests.
    """
    override = os.environ.get(DATA_DIR_ENV)
    if override:
        base = Path(override)
    else:
        local = os.environ.get("LOCALAPPDATA") or str(Path.home() / "AppData" / "Local")
        base = Path(local) / APP_DIR_NAME
    base.mkdir(parents=True, exist_ok=True)
    return base


def db_path() -> Path:
    return data_dir() / "library.sqlite3"


def log_dir() -> Path:
    d = data_dir() / "logs"
    d.mkdir(parents=True, exist_ok=True)
    return d
