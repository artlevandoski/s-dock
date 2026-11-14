import hashlib
import os

from switchyard.scanner import apply_folder_generation, classify_file, import_entries, scan_folder


def by_name(result):
    return {e.filename: e for e in result.entries}


def test_preview_classification(game_folder):
    result = scan_folder(str(game_folder), "switch1")
    entries = by_name(result)
    assert len(entries) == 8  # notes.txt ignored

    base = entries["Super Placeholder Quest [0100AAAA00000000][v0].nsp"]
    assert base.kind == "base" and base.title == "Super Placeholder Quest"
    assert base.title_id == "0100AAAA00000000"
    assert base.generation == "switch1" and base.include and not base.flags

    upd = entries["Super Placeholder Quest [0100AAAA00000800][v131072].nsp"]
    assert upd.kind == "update" and "patch" in upd.flags and not upd.include

    dlc = entries["Super Placeholder Quest Bonus Pack [0100AAAA00001001][v0].nsp"]
    assert dlc.kind == "dlc" and "dlc" in dlc.flags and not dlc.include

    aca = entries["Arcade Archives SAMPLE BLASTER [0100BBBB00000000][v0].nsp"]
    assert aca.title == "Arcade Archives SAMPLE BLASTER" and aca.include

    pair = [entries["Another Test Game (copy).xci"], entries["Another Test Game.xci"]]
    assert sorted("duplicate" in e.flags for e in pair) == [False, True]
    assert all(e.include != ("duplicate" in e.flags) for e in pair)

    ambiguous = entries["0100CCCC00000000.nsp"]
    assert ambiguous.generation == "unclassified" and "ambiguous_name" in ambiguous.flags

    # Filename says Switch 2 but the folder was assigned Switch 1 -> unclassified for review.
    s2 = entries["Puzzle Thing (Switch 2 Edition).nsp"]
    assert s2.title == "Puzzle Thing Switch 2 Edition"
    assert s2.generation == "unclassified" and "generation_conflict" in s2.flags


def test_folder_generation_reassignment_respects_manual_edits(game_folder):
    result = scan_folder(str(game_folder), "unclassified")
    entries = by_name(result)
    s2 = entries["Puzzle Thing (Switch 2 Edition).nsp"]
    assert s2.generation == "switch2"  # filename hint with no folder assignment
    manual = entries["Arcade Archives SAMPLE BLASTER [0100BBBB00000000][v0].nsp"]
    manual.generation, manual.generation_source = "switch2", "manual"

    apply_folder_generation(result.entries, "switch1")
    assert entries["Super Placeholder Quest [0100AAAA00000000][v0].nsp"].generation == "switch1"
    assert manual.generation == "switch2"
    assert entries["0100CCCC00000000.nsp"].generation == "unclassified"


def test_unknown_files_default_to_unclassified():
    e = classify_file(r"C:\x\Some Game.nsp")
    assert e.kind == "base" and e.generation_hint is None and not e.ambiguous_name
    apply_folder_generation([e], "unclassified")
    assert e.generation == "unclassified"


def test_scan_leaves_files_untouched(game_folder):
    def snapshot():
        out = {}
        for root, _dirs, files in os.walk(game_folder):
            for f in files:
                p = os.path.join(root, f)
                st = os.stat(p)
                out[p] = (st.st_size, st.st_mtime_ns, hashlib.sha1(open(p, "rb").read()).hexdigest())
        return out

    before = snapshot()
    result = scan_folder(str(game_folder), "switch1")
    assert snapshot() == before
    assert len(result.entries) == 8


def test_repeat_scan_preserves_records(game_folder, library):
    first = scan_folder(str(game_folder), "switch1", library)
    summary = import_entries(library, str(game_folder), "switch1", first.entries)
    assert len(summary.added) == 5  # 8 found - update - dlc - duplicate
    ids_before = {e.id: (e.title, e.generation) for e in library.list_editions()}

    # User edits a generation; a rescan must not overwrite it or duplicate anything.
    some_id = next(iter(ids_before))
    library.set_generation(some_id, "switch2")

    second = scan_folder(str(game_folder), "switch1", library)
    assert sum(e.in_library for e in second.entries) == 5
    assert all(not e.include for e in second.entries if e.in_library)
    summary2 = import_entries(library, str(game_folder), "switch1", second.entries)
    assert summary2.added == [] and summary2.already_present == 5

    after = {e.id: e for e in library.list_editions()}
    assert set(after) == set(ids_before)
    assert after[some_id].generation == "switch2"


def test_missing_paths_reported_on_rescan(game_folder, library):
    result = scan_folder(str(game_folder), "switch1", library)
    import_entries(library, str(game_folder), "switch1", result.entries)
    moved = game_folder / "Arcade Archives SAMPLE BLASTER [0100BBBB00000000][v0].nsp"
    moved.rename(game_folder.parent / "moved.nsp")

    rescan = scan_folder(str(game_folder), "switch1", library)
    assert str(moved) in rescan.missing_known
    missing = [e for e in library.list_editions() if e.is_missing]
    assert [e.title for e in missing] == ["Arcade Archives SAMPLE BLASTER"]
