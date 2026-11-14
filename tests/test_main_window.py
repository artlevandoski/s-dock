"""End-to-end check of the Milestone 1 flow with a fake emulator:
import a folder, restart the app, Play, emulator exits, same cover is selected."""
from switchyard.db import Library
from switchyard.scanner import import_entries, scan_folder
from switchyard.ui.main_window import MainWindow


def open_window(qtbot, db):
    lib = Library(db)
    win = MainWindow(lib)
    qtbot.addWidget(win)
    win.show()
    return lib, win


def test_import_restart_play_and_return_to_cover(qtbot, game_folder, data_dir, fake_profile):
    db = data_dir / "library.sqlite3"

    # Session 1: import and configure.
    lib, win = open_window(qtbot, db)
    assert win.model.rowCount() == 0
    result = scan_folder(str(game_folder), "switch1", lib)
    import_entries(lib, str(game_folder), "switch1", result.entries)
    lib.save_profile(fake_profile(sleep=0.3))
    win.refresh()
    assert win.model.rowCount() == 5
    target = next(e for e in lib.list_editions() if e.title == "Super Placeholder Quest")
    assert win.select_edition(target.id)
    win.close()
    lib.close()

    # Session 2: restart restores library, profile and selection.
    lib, win = open_window(qtbot, db)
    assert win.model.rowCount() == 5
    assert win.current_edition_id() == target.id
    assert lib.list_profiles()[0].name == "Fake Eden"

    with qtbot.waitSignal(win.launcher.launched, timeout=10000):
        assert win.play_selected()
    assert not win.play_btn.isEnabled()
    # Simulate the user wandering off to another cover while the emulator runs.
    other = next(e for e in lib.list_editions() if e.id != target.id)
    win.select_edition(other.id)
    with qtbot.waitSignal(win.launcher.finished, timeout=15000):
        pass
    qtbot.wait(300)  # deferred focus restore
    assert win.current_edition_id() == target.id
    assert win.play_btn.isEnabled() and win.play_btn.text() == "Play"
    assert win.grid.hasFocus() or win.focusWidget() is win.grid
    win.close()
    lib.close()


def test_missing_file_shown_and_app_stays_usable(qtbot, game_folder, data_dir):
    db = data_dir / "library.sqlite3"
    lib, win = open_window(qtbot, db)
    result = scan_folder(str(game_folder), "switch1", lib)
    import_entries(lib, str(game_folder), "switch1", result.entries)
    gone = next(e for e in lib.list_editions() if e.title.startswith("Arcade"))
    gone.primary_file and __import__("os").remove(gone.primary_file.path)
    win.refresh(select_id=gone.id)
    assert win.current_edition().is_missing
    assert win.locate_btn.isVisibleTo(win) and win.detail_missing.isVisibleTo(win)
    assert "with missing files" in win.statusBar().currentMessage()
    win.close()
    lib.close()
