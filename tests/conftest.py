import os
import sys
from pathlib import Path

os.environ.setdefault("QT_QPA_PLATFORM", "offscreen")

import pytest  # noqa: E402

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from switchyard.db import EmulatorProfile, Library  # noqa: E402

FAKE_EMULATOR = Path(__file__).with_name("fake_emulator.py")

# Placeholder files only; contents are irrelevant to the scanner.
SAMPLE_FILES = [
    "Super Placeholder Quest [0100AAAA00000000][v0].nsp",
    "Super Placeholder Quest [0100AAAA00000800][v131072].nsp",
    "Super Placeholder Quest Bonus Pack [0100AAAA00001001][v0].nsp",
    "Arcade Archives SAMPLE BLASTER [0100BBBB00000000][v0].nsp",
    "sub folder with spaces/Another Test Game.xci",
    "Another Test Game (copy).xci",
    "Puzzle Thing (Switch 2 Edition).nsp",
    "0100CCCC00000000.nsp",
    "notes.txt",
]


@pytest.fixture
def data_dir(tmp_path, monkeypatch):
    d = tmp_path / "app data"
    monkeypatch.setenv("SWITCHYARD_DATA_DIR", str(d))
    return d


@pytest.fixture
def game_folder(tmp_path):
    root = tmp_path / "My Games" / "Switch Library"
    for rel in SAMPLE_FILES:
        p = root / rel
        p.parent.mkdir(parents=True, exist_ok=True)
        p.write_bytes(b"placeholder game file for tests")
    return root


@pytest.fixture
def library(data_dir):
    lib = Library(data_dir / "library.sqlite3")
    yield lib
    lib.close()


@pytest.fixture
def fake_profile(tmp_path):
    """Profile that runs the fake emulator via the current Python interpreter."""
    record = tmp_path / "launch record.json"
    exe_dir = tmp_path / "Emulator Folder With Spaces"
    exe_dir.mkdir()

    def make(exit_code=0, sleep=0.0):
        return EmulatorProfile.new(
            name="Fake Eden",
            executable=sys.executable,
            working_dir=str(exe_dir),
            generation="switch1",
            args=[str(FAKE_EMULATOR), "--record", str(record), "--exit-code", str(exit_code),
                  "--sleep", str(sleep), "-g", "{game}"],
        )

    make.record = record
    make.exe_dir = exe_dir
    return make
