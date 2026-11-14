"""Folder scanning and import preview.

The scanner only *reads* directory listings and file sizes. It never opens,
renames, moves, or modifies game files.
"""
from __future__ import annotations

import os
import re
from dataclasses import dataclass, field

from .db import Library

GAME_EXTENSIONS = (".nsp", ".xci", ".nsz", ".xcz")

TITLE_ID_RE = re.compile(r"\[([0-9A-Fa-f]{16})\]")
VERSION_RE = re.compile(r"\[v(\d+)\]|(?<![A-Za-z0-9])v(\d{5,})(?![0-9])", re.IGNORECASE)
UPDATE_WORD_RE = re.compile(r"(?<![A-Za-z])(update|upd|patch)(?![A-Za-z])", re.IGNORECASE)
DLC_WORD_RE = re.compile(r"(?<![A-Za-z])(dlc|add[- ]?on)(?![A-Za-z])", re.IGNORECASE)
SWITCH2_HINT_RE = re.compile(r"switch[\s_-]*2(?![0-9])|(?<![A-Za-z0-9])ns2(?![A-Za-z0-9])", re.IGNORECASE)
SWITCH1_HINT_RE = re.compile(r"[\[(]\s*(?:switch[\s_-]*1|ns1)\s*[\])]", re.IGNORECASE)
KEEP_GROUP_RE = re.compile(r"switch[\s_-]*2\s+edition", re.IGNORECASE)
GROUP_RE = re.compile(r"\[[^\]]*\]|\([^)]*\)|\{[^}]*\}")
GENERIC_NAMES = {"game", "rom", "untitled", "unknown", "file", "new", "copy", "test"}

# Flags shown in the preview. Entries with these are not imported by default.
REVIEW_FLAGS = {"patch", "dlc", "duplicate"}
FLAG_LABELS = {
    "patch": "Suspected update/patch",
    "dlc": "Suspected DLC",
    "duplicate": "Possible duplicate",
    "ambiguous_name": "Ambiguous filename",
    "generation_conflict": "Filename and folder generation disagree",
    "in_library": "Already in library",
}


@dataclass
class ScanEntry:
    path: str
    filename: str
    title: str
    title_id: str | None
    version: int | None
    kind: str  # base / update / dlc
    generation_hint: str | None  # from filename, if any
    ambiguous_name: bool
    size_bytes: int
    generation: str = "unclassified"
    generation_source: str = "none"  # none / folder / filename / ambiguous / conflict / manual
    flags: set[str] = field(default_factory=set)
    existing_edition_id: str | None = None
    include: bool = True

    @property
    def in_library(self) -> bool:
        return self.existing_edition_id is not None

    def notes(self) -> str:
        order = ["in_library", "patch", "dlc", "duplicate", "ambiguous_name", "generation_conflict"]
        return "; ".join(FLAG_LABELS[f] for f in order if f in self.flags)


@dataclass
class ScanResult:
    folder: str
    entries: list[ScanEntry]
    missing_known: list[str]  # library files under this folder that no longer exist
    errors: list[str]

    @property
    def new_entries(self) -> list[ScanEntry]:
        return [e for e in self.entries if not e.in_library]


def clean_title(stem: str) -> str:
    def repl(m: re.Match) -> str:
        group = m.group(0)
        return " " + group[1:-1] + " " if KEEP_GROUP_RE.search(group) else " "

    s = GROUP_RE.sub(repl, stem)
    s = s.replace("_", " ")
    s = re.sub(r"\s+", " ", s).strip(" -.")
    return s


def normalized_title(title: str) -> str:
    return re.sub(r"[^a-z0-9]", "", title.lower())


def is_ambiguous_title(title: str) -> bool:
    letters = sum(ch.isalpha() for ch in title)
    if letters < 2:
        return True
    if re.fullmatch(r"[0-9A-Fa-f]{8,}", title.replace(" ", "")):
        return True
    return title.strip().lower() in GENERIC_NAMES


def kind_from_title_id(tid: str) -> str:
    t = tid.upper()
    if t.endswith("800"):
        return "update"
    if t.endswith("000"):
        return "base"
    return "dlc"


def resolve_generation(hint: str | None, ambiguous: bool, folder_generation: str) -> tuple[str, str]:
    """Return (generation, source) for an entry that the user has not edited."""
    if ambiguous:
        return "unclassified", "ambiguous"
    if hint and folder_generation != "unclassified" and hint != folder_generation:
        return "unclassified", "conflict"
    if hint:
        return hint, "filename"
    if folder_generation != "unclassified":
        return folder_generation, "folder"
    return "unclassified", "none"


def classify_file(path: str, size_bytes: int = 0) -> ScanEntry:
    filename = os.path.basename(path)
    stem, _ext = os.path.splitext(filename)

    tid_match = TITLE_ID_RE.search(stem)
    title_id = tid_match.group(1).upper() if tid_match else None
    ver_match = VERSION_RE.search(stem)
    version = int(ver_match.group(1) or ver_match.group(2)) if ver_match else None

    if title_id:
        kind = kind_from_title_id(title_id)
    elif DLC_WORD_RE.search(stem):
        kind = "dlc"
    elif UPDATE_WORD_RE.search(stem) or (version is not None and version > 0):
        kind = "update"
    else:
        kind = "base"

    hint = None
    if SWITCH2_HINT_RE.search(stem):
        hint = "switch2"
    elif SWITCH1_HINT_RE.search(stem):
        hint = "switch1"

    title = clean_title(stem) or stem
    ambiguous = is_ambiguous_title(title)
    entry = ScanEntry(
        path=os.path.abspath(path),
        filename=filename,
        title=title,
        title_id=title_id,
        version=version,
        kind=kind,
        generation_hint=hint,
        ambiguous_name=ambiguous,
        size_bytes=size_bytes,
    )
    if kind == "update":
        entry.flags.add("patch")
    elif kind == "dlc":
        entry.flags.add("dlc")
    if ambiguous:
        entry.flags.add("ambiguous_name")
    return entry


def apply_folder_generation(entries: list[ScanEntry], folder_generation: str) -> None:
    """(Re)assign the folder generation to every entry the user hasn't edited."""
    for e in entries:
        if e.generation_source == "manual":
            continue
        e.generation, e.generation_source = resolve_generation(
            e.generation_hint, e.ambiguous_name, folder_generation
        )
        if e.generation_source == "conflict":
            e.flags.add("generation_conflict")
        else:
            e.flags.discard("generation_conflict")


def _iter_game_files(folder: str, recursive: bool, errors: list[str]):
    def onerror(err: OSError) -> None:
        errors.append(f"{err.filename}: {err.strerror}")

    walker = os.walk(folder, onerror=onerror)
    for root, dirs, files in walker:
        dirs.sort(key=str.lower)
        for name in sorted(files, key=str.lower):
            if name.lower().endswith(GAME_EXTENSIONS):
                yield os.path.join(root, name)
        if not recursive:
            break


def scan_folder(
    folder: str,
    folder_generation: str = "unclassified",
    library: Library | None = None,
    recursive: bool = True,
) -> ScanResult:
    folder = os.path.abspath(folder)
    errors: list[str] = []
    entries: list[ScanEntry] = []
    if not os.path.isdir(folder):
        return ScanResult(folder, [], [], [f"Folder not found: {folder}"])

    for path in _iter_game_files(folder, recursive, errors):
        try:
            size = os.path.getsize(path)
        except OSError as exc:
            errors.append(f"{path}: {exc.strerror}")
            continue
        entries.append(classify_file(path, size))

    apply_folder_generation(entries, folder_generation)

    # Duplicate detection: against existing library editions, then within the scan.
    seen: dict[str, str] = {}
    if library is not None:
        for ed in library.list_editions():
            if ed.content_kind != "base":
                continue
            if ed.console_title_id:
                seen.setdefault("tid:" + ed.console_title_id.upper(), ed.id)
            seen.setdefault("title:" + normalized_title(ed.title), ed.id)

    for e in entries:
        if library is not None:
            e.existing_edition_id = library.find_edition_by_path(e.path)
        if e.in_library:
            e.flags.add("in_library")
            e.include = False
            continue
        if e.kind == "base":
            keys = []
            if e.title_id:
                keys.append("tid:" + e.title_id)
            if not e.ambiguous_name:
                keys.append("title:" + normalized_title(e.title))
            if any(k in seen for k in keys):
                e.flags.add("duplicate")
            for k in keys:
                seen.setdefault(k, e.path)
        e.include = not (e.flags & REVIEW_FLAGS)

    missing: list[str] = []
    if library is not None:
        missing = [f.path for f in library.files_under(folder) if not f.exists]

    return ScanResult(folder, entries, missing, errors)


@dataclass
class ImportSummary:
    added: list[str]
    already_present: int
    skipped: int


def import_entries(
    library: Library, folder: str, folder_generation: str, entries: list[ScanEntry]
) -> ImportSummary:
    """Write the chosen preview entries to the library. Existing records are preserved."""
    folder_id = library.upsert_folder(folder, folder_generation)
    added: list[str] = []
    present: list[str] = []
    skipped = 0
    for e in entries:
        # Re-check at import time so repeat imports can never create duplicates.
        existing = e.existing_edition_id or library.find_edition_by_path(e.path)
        if existing:
            present.append(e.path)
            continue
        if not e.include:
            skipped += 1
            continue
        added.append(
            library.add_edition(
                title=e.title,
                generation=e.generation,
                path=e.path,
                content_kind=e.kind,
                console_title_id=e.title_id,
                folder_id=folder_id,
                size_bytes=e.size_bytes,
            )
        )
    library.mark_seen(present)
    return ImportSummary(added=added, already_present=len(present), skipped=skipped)
