"""SQLite persistence for the library and emulator profiles.

Design notes (Milestone 1):
* Editions have stable internal UUIDs. Local files live in ``edition_files`` so an
  edition can later own several files (alternate versions, updates, DLC).
* ``console_title_id`` and ``igdb_id`` are reserved, nullable fields.
* Generation (Switch 1 / Switch 2 / unclassified) belongs to the edition and is
  independent of emulator profiles.
"""
from __future__ import annotations

import json
import os
import sqlite3
import uuid
from dataclasses import dataclass, field
from datetime import datetime, timezone
from pathlib import Path

SCHEMA_VERSION = 1

GENERATIONS = ("switch1", "switch2", "unclassified")
GENERATION_LABELS = {"switch1": "Switch 1", "switch2": "Switch 2", "unclassified": "Unclassified"}
PROFILE_GENERATIONS = ("switch1", "switch2", "both")
PROFILE_GENERATION_LABELS = {"switch1": "Switch 1", "switch2": "Switch 2", "both": "Switch 1 and Switch 2"}
CONTENT_KINDS = ("base", "update", "dlc")
CONTENT_KIND_LABELS = {"base": "Base game", "update": "Update / patch", "dlc": "DLC / add-on"}

SCHEMA = """
CREATE TABLE IF NOT EXISTS meta (
    key   TEXT PRIMARY KEY,
    value TEXT
);
CREATE TABLE IF NOT EXISTS library_folders (
    id              INTEGER PRIMARY KEY AUTOINCREMENT,
    path            TEXT NOT NULL,
    path_key        TEXT NOT NULL UNIQUE,
    generation      TEXT NOT NULL DEFAULT 'unclassified',
    added_at        TEXT NOT NULL,
    last_scanned_at TEXT
);
CREATE TABLE IF NOT EXISTS editions (
    id               TEXT PRIMARY KEY,
    title            TEXT NOT NULL,
    generation       TEXT NOT NULL DEFAULT 'unclassified'
                     CHECK (generation IN ('switch1', 'switch2', 'unclassified')),
    content_kind     TEXT NOT NULL DEFAULT 'base',
    console_title_id TEXT,
    igdb_id          INTEGER,
    created_at       TEXT NOT NULL,
    updated_at       TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS edition_files (
    id           INTEGER PRIMARY KEY AUTOINCREMENT,
    edition_id   TEXT NOT NULL REFERENCES editions(id) ON DELETE CASCADE,
    folder_id    INTEGER REFERENCES library_folders(id) ON DELETE SET NULL,
    path         TEXT NOT NULL,
    path_key     TEXT NOT NULL UNIQUE,
    role         TEXT NOT NULL DEFAULT 'primary',
    size_bytes   INTEGER,
    added_at     TEXT NOT NULL,
    last_seen_at TEXT
);
CREATE INDEX IF NOT EXISTS idx_edition_files_edition ON edition_files(edition_id);
CREATE TABLE IF NOT EXISTS emulator_profiles (
    id          TEXT PRIMARY KEY,
    name        TEXT NOT NULL,
    emulator    TEXT NOT NULL DEFAULT 'custom',
    executable  TEXT NOT NULL,
    working_dir TEXT NOT NULL DEFAULT '',
    generation  TEXT NOT NULL DEFAULT 'switch1',
    args_json   TEXT NOT NULL DEFAULT '[]',
    created_at  TEXT NOT NULL,
    updated_at  TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS settings (
    key   TEXT PRIMARY KEY,
    value TEXT
);
"""


def utc_now() -> str:
    return datetime.now(timezone.utc).isoformat(timespec="seconds")


def path_key(path: str | os.PathLike) -> str:
    """Case- and separator-insensitive key used to recognise the same file."""
    return os.path.normcase(os.path.normpath(os.path.abspath(os.fspath(path))))


@dataclass
class EditionFile:
    id: int
    edition_id: str
    path: str
    role: str = "primary"

    @property
    def exists(self) -> bool:
        return os.path.isfile(self.path)


@dataclass
class Edition:
    id: str
    title: str
    generation: str = "unclassified"
    content_kind: str = "base"
    console_title_id: str | None = None
    igdb_id: int | None = None
    files: list[EditionFile] = field(default_factory=list)

    @property
    def primary_file(self) -> EditionFile | None:
        for f in self.files:
            if f.role == "primary":
                return f
        return self.files[0] if self.files else None

    @property
    def is_missing(self) -> bool:
        pf = self.primary_file
        return pf is None or not pf.exists


@dataclass
class EmulatorProfile:
    id: str
    name: str
    executable: str
    working_dir: str = ""
    generation: str = "switch1"
    args: list[str] = field(default_factory=list)
    emulator: str = "custom"

    @classmethod
    def new(cls, **kwargs) -> "EmulatorProfile":
        return cls(id=str(uuid.uuid4()), **kwargs)

    def supports(self, generation: str) -> bool:
        return self.generation == "both" or self.generation == generation


class Library:
    def __init__(self, db_file: str | os.PathLike):
        self.db_file = str(db_file)
        Path(self.db_file).parent.mkdir(parents=True, exist_ok=True)
        self.conn = sqlite3.connect(self.db_file)
        self.conn.row_factory = sqlite3.Row
        self.conn.execute("PRAGMA foreign_keys = ON")
        self.conn.executescript(SCHEMA)
        with self.conn:
            self.conn.execute(
                "INSERT OR IGNORE INTO meta(key, value) VALUES ('schema_version', ?)",
                (str(SCHEMA_VERSION),),
            )

    def close(self) -> None:
        self.conn.close()

    # ------------------------------------------------------------------ folders
    def upsert_folder(self, path: str, generation: str) -> int:
        key = path_key(path)
        ts = utc_now()
        with self.conn:
            self.conn.execute(
                """INSERT INTO library_folders(path, path_key, generation, added_at, last_scanned_at)
                   VALUES (?, ?, ?, ?, ?)
                   ON CONFLICT(path_key) DO UPDATE SET
                       generation = excluded.generation,
                       last_scanned_at = excluded.last_scanned_at""",
                (os.path.abspath(path), key, generation, ts, ts),
            )
        row = self.conn.execute("SELECT id FROM library_folders WHERE path_key = ?", (key,)).fetchone()
        return int(row["id"])

    def list_folders(self) -> list[dict]:
        return [dict(r) for r in self.conn.execute("SELECT * FROM library_folders ORDER BY path")]

    # ----------------------------------------------------------------- editions
    def _files_by_edition(self) -> dict[str, list[EditionFile]]:
        out: dict[str, list[EditionFile]] = {}
        for r in self.conn.execute("SELECT id, edition_id, path, role FROM edition_files ORDER BY id"):
            out.setdefault(r["edition_id"], []).append(
                EditionFile(id=r["id"], edition_id=r["edition_id"], path=r["path"], role=r["role"])
            )
        return out

    @staticmethod
    def _edition_from_row(r: sqlite3.Row, files: list[EditionFile]) -> Edition:
        return Edition(
            id=r["id"],
            title=r["title"],
            generation=r["generation"],
            content_kind=r["content_kind"],
            console_title_id=r["console_title_id"],
            igdb_id=r["igdb_id"],
            files=files,
        )

    def list_editions(self) -> list[Edition]:
        files = self._files_by_edition()
        rows = self.conn.execute("SELECT * FROM editions ORDER BY title COLLATE NOCASE, id")
        return [self._edition_from_row(r, files.get(r["id"], [])) for r in rows]

    def get_edition(self, edition_id: str) -> Edition | None:
        r = self.conn.execute("SELECT * FROM editions WHERE id = ?", (edition_id,)).fetchone()
        if r is None:
            return None
        files = [
            EditionFile(id=f["id"], edition_id=f["edition_id"], path=f["path"], role=f["role"])
            for f in self.conn.execute(
                "SELECT id, edition_id, path, role FROM edition_files WHERE edition_id = ? ORDER BY id",
                (edition_id,),
            )
        ]
        return self._edition_from_row(r, files)

    def find_edition_by_path(self, path: str) -> str | None:
        r = self.conn.execute(
            "SELECT edition_id FROM edition_files WHERE path_key = ?", (path_key(path),)
        ).fetchone()
        return r["edition_id"] if r else None

    def add_edition(
        self,
        title: str,
        generation: str,
        path: str,
        content_kind: str = "base",
        console_title_id: str | None = None,
        folder_id: int | None = None,
        size_bytes: int | None = None,
    ) -> str:
        if generation not in GENERATIONS:
            raise ValueError(f"Unknown generation: {generation}")
        edition_id = str(uuid.uuid4())
        ts = utc_now()
        with self.conn:
            self.conn.execute(
                """INSERT INTO editions(id, title, generation, content_kind, console_title_id,
                                        igdb_id, created_at, updated_at)
                   VALUES (?, ?, ?, ?, ?, NULL, ?, ?)""",
                (edition_id, title, generation, content_kind, console_title_id, ts, ts),
            )
            self.conn.execute(
                """INSERT INTO edition_files(edition_id, folder_id, path, path_key, role,
                                             size_bytes, added_at, last_seen_at)
                   VALUES (?, ?, ?, ?, 'primary', ?, ?, ?)""",
                (edition_id, folder_id, os.path.abspath(path), path_key(path), size_bytes, ts, ts),
            )
        return edition_id

    def set_generation(self, edition_id: str, generation: str) -> None:
        if generation not in GENERATIONS:
            raise ValueError(f"Unknown generation: {generation}")
        with self.conn:
            self.conn.execute(
                "UPDATE editions SET generation = ?, updated_at = ? WHERE id = ?",
                (generation, utc_now(), edition_id),
            )

    def relink_file(self, file_id: int, new_path: str) -> None:
        key = path_key(new_path)
        other = self.conn.execute(
            "SELECT id FROM edition_files WHERE path_key = ? AND id != ?", (key, file_id)
        ).fetchone()
        if other:
            raise ValueError("That file already belongs to another library entry.")
        with self.conn:
            self.conn.execute(
                "UPDATE edition_files SET path = ?, path_key = ?, last_seen_at = ? WHERE id = ?",
                (os.path.abspath(new_path), key, utc_now(), file_id),
            )

    def mark_seen(self, paths: list[str]) -> None:
        ts = utc_now()
        with self.conn:
            self.conn.executemany(
                "UPDATE edition_files SET last_seen_at = ? WHERE path_key = ?",
                [(ts, path_key(p)) for p in paths],
            )

    def files_under(self, folder: str) -> list[EditionFile]:
        prefix = path_key(folder).rstrip("\\/") + os.sep
        return [
            EditionFile(id=r["id"], edition_id=r["edition_id"], path=r["path"], role=r["role"])
            for r in self.conn.execute("SELECT id, edition_id, path, path_key, role FROM edition_files")
            if r["path_key"].startswith(prefix)
        ]

    # ----------------------------------------------------------------- settings
    def get_setting(self, key: str, default: str | None = None) -> str | None:
        r = self.conn.execute("SELECT value FROM settings WHERE key = ?", (key,)).fetchone()
        return r["value"] if r else default

    def set_setting(self, key: str, value: str | None) -> None:
        with self.conn:
            self.conn.execute(
                "INSERT INTO settings(key, value) VALUES (?, ?) "
                "ON CONFLICT(key) DO UPDATE SET value = excluded.value",
                (key, value),
            )

    # ----------------------------------------------------------------- profiles
    @staticmethod
    def _profile_from_row(r: sqlite3.Row) -> EmulatorProfile:
        return EmulatorProfile(
            id=r["id"],
            name=r["name"],
            emulator=r["emulator"],
            executable=r["executable"],
            working_dir=r["working_dir"] or "",
            generation=r["generation"],
            args=list(json.loads(r["args_json"] or "[]")),
        )

    def list_profiles(self) -> list[EmulatorProfile]:
        rows = self.conn.execute("SELECT * FROM emulator_profiles ORDER BY created_at, name")
        return [self._profile_from_row(r) for r in rows]

    def get_profile(self, profile_id: str) -> EmulatorProfile | None:
        r = self.conn.execute("SELECT * FROM emulator_profiles WHERE id = ?", (profile_id,)).fetchone()
        return self._profile_from_row(r) if r else None

    def save_profile(self, p: EmulatorProfile) -> None:
        if p.generation not in PROFILE_GENERATIONS:
            raise ValueError(f"Unknown profile generation: {p.generation}")
        ts = utc_now()
        with self.conn:
            self.conn.execute(
                """INSERT INTO emulator_profiles(id, name, emulator, executable, working_dir,
                                                 generation, args_json, created_at, updated_at)
                   VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)
                   ON CONFLICT(id) DO UPDATE SET
                       name = excluded.name, emulator = excluded.emulator,
                       executable = excluded.executable, working_dir = excluded.working_dir,
                       generation = excluded.generation, args_json = excluded.args_json,
                       updated_at = excluded.updated_at""",
                (p.id, p.name, p.emulator, p.executable, p.working_dir, p.generation,
                 json.dumps(list(p.args)), ts, ts),
            )

    def delete_profile(self, profile_id: str) -> None:
        with self.conn:
            self.conn.execute("DELETE FROM emulator_profiles WHERE id = ?", (profile_id,))
