import json
import os

import pytest

from switchyard.db import EmulatorProfile
from switchyard.launcher import (
    EDEN_PRESET_ARGS,
    LaunchController,
    build_arguments,
    effective_working_dir,
    validate_launch,
)


@pytest.fixture
def spaced_game(tmp_path):
    p = tmp_path / "Games With Spaces" / "My Game (Rev 1) [0100AAAA00000000].nsp"
    p.parent.mkdir()
    p.write_bytes(b"placeholder")
    return str(p)


def test_arguments_keep_spaces_intact(spaced_game):
    args = build_arguments(EDEN_PRESET_ARGS, spaced_game)
    assert args == ["-g", spaced_game]  # one argument, not split on spaces


def test_working_dir_defaults_to_exe_folder():
    p = EmulatorProfile.new(name="x", executable=r"C:\Emu Dir\eden.exe", args=[])
    assert effective_working_dir(p) == r"C:\Emu Dir"


def test_validation_reports_missing_executable_and_game(tmp_path):
    p = EmulatorProfile.new(name="x", executable=str(tmp_path / "nope" / "eden.exe"), args=["-g", "{game}"])
    problems = validate_launch(p, str(tmp_path / "missing game.nsp"))
    assert any("executable not found" in m for m in problems)
    assert any("Game file not found" in m for m in problems)
    assert validate_launch(None, "x")[0].startswith("No emulator")


def test_validation_requires_game_placeholder(fake_profile, spaced_game):
    p = fake_profile()
    p.args = ["--fullscreen"]
    assert any("{game}" in m for m in validate_launch(p, spaced_game))


def test_launch_completes_with_test_process(qtbot, fake_profile, spaced_game):
    ctl = LaunchController()
    profile = fake_profile(exit_code=3, sleep=0.3)
    with qtbot.waitSignal(ctl.launched, timeout=10000):
        assert ctl.launch(profile, spaced_game, "edition-1")
    assert ctl.is_running()
    with qtbot.waitSignal(ctl.finished, timeout=15000) as blocker:
        pass
    assert blocker.args == ["edition-1", 3, False]
    assert not ctl.is_running()
    rec = json.loads(fake_profile.record.read_text(encoding="utf-8"))
    assert rec["game"] == spaced_game and rec["game_exists"]
    assert os.path.normcase(rec["cwd"]) == os.path.normcase(str(fake_profile.exe_dir))


def test_missing_executable_fails_without_starting(qtbot, tmp_path, spaced_game):
    ctl = LaunchController()
    p = EmulatorProfile.new(name="x", executable=str(tmp_path / "missing" / "eden.exe"), args=["-g", "{game}"])
    with qtbot.waitSignal(ctl.failed, timeout=2000) as blocker:
        assert ctl.launch(p, spaced_game, "e1") is False
    assert "not found" in blocker.args[1]
    assert not ctl.is_running()


def test_missing_game_fails_without_starting(qtbot, fake_profile, tmp_path):
    ctl = LaunchController()
    with qtbot.waitSignal(ctl.failed, timeout=2000) as blocker:
        assert ctl.launch(fake_profile(), str(tmp_path / "gone.nsp"), "e1") is False
    assert "Game file not found" in blocker.args[1]


def test_startup_failure_is_reported(qtbot, tmp_path, spaced_game):
    bogus = tmp_path / "Not Really An Emulator.exe"
    bogus.write_text("this is not a program")
    ctl = LaunchController()
    p = EmulatorProfile.new(name="x", executable=str(bogus), args=["-g", "{game}"])
    with qtbot.waitSignal(ctl.failed, timeout=10000) as blocker:
        assert ctl.launch(p, spaced_game, "e1")
    assert blocker.args[0] == "e1" and "could not be started" in blocker.args[1]
    assert not ctl.is_running()


def test_second_launch_rejected_while_running(qtbot, fake_profile, spaced_game):
    ctl = LaunchController()
    with qtbot.waitSignal(ctl.launched, timeout=10000):
        ctl.launch(fake_profile(sleep=1.0), spaced_game, "a")
    with qtbot.waitSignal(ctl.failed, timeout=2000):
        assert ctl.launch(fake_profile(), spaced_game, "b") is False
    with qtbot.waitSignal(ctl.finished, timeout=15000):
        pass
