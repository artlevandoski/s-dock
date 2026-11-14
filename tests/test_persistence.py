from switchyard.db import EmulatorProfile, Library
from switchyard.scanner import import_entries, scan_folder


def test_library_and_profile_survive_restart(game_folder, data_dir):
    db = data_dir / "library.sqlite3"
    lib = Library(db)
    result = scan_folder(str(game_folder), "switch1", lib)
    import_entries(lib, str(game_folder), "switch1", result.entries)
    profile = EmulatorProfile.new(
        name="Eden", executable=r"C:\Program Files\Eden Emulator\eden.exe",
        working_dir="", generation="switch1", args=["-g", "{game}"], emulator="eden",
    )
    lib.save_profile(profile)
    lib.set_setting("last_selected_edition", lib.list_editions()[1].id)
    snapshot = [(e.id, e.title, e.generation, e.primary_file.path) for e in lib.list_editions()]
    lib.close()

    reopened = Library(db)
    try:
        assert [(e.id, e.title, e.generation, e.primary_file.path) for e in reopened.list_editions()] == snapshot
        [p] = reopened.list_profiles()
        assert p == profile
        assert reopened.get_setting("last_selected_edition") == snapshot[1][0]
        assert reopened.list_folders()[0]["generation"] == "switch1"
    finally:
        reopened.close()


def test_reserved_fields_and_separate_file_table(library, tmp_path):
    game = tmp_path / "A Game.nsp"
    game.write_bytes(b"x")
    eid = library.add_edition("A Game", "switch2", str(game))
    e = library.get_edition(eid)
    assert e.console_title_id is None and e.igdb_id is None
    assert len(e.files) == 1 and e.primary_file.path == str(game)
    cols = {r[1] for r in library.conn.execute("PRAGMA table_info(editions)")}
    assert {"console_title_id", "igdb_id"} <= cols
    assert "path" not in cols  # launch paths live in edition_files


def test_relink_moved_file(library, tmp_path):
    old = tmp_path / "old place" / "Game.nsp"
    eid = library.add_edition("Game", "switch1", str(old))
    assert library.get_edition(eid).is_missing
    new = tmp_path / "new place" / "Game.nsp"
    new.parent.mkdir()
    new.write_bytes(b"x")
    library.relink_file(library.get_edition(eid).primary_file.id, str(new))
    e = library.get_edition(eid)
    assert not e.is_missing and e.primary_file.path == str(new)
