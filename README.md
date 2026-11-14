# Switch Yard

An independent Windows frontend for certain game libraries. You bring your own game files and
your own emulator installation. Switch Yard does not include or download games, firmware,
keys, or system files.

**Status:** Milestone 1. Import a folder, browse the cover grid, configure one emulator
profile, and Play. Built with C# / .NET 10 and [Avalonia](https://avaloniaui.net/).

## Requirements

- Windows 10 or 11
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (for building; a
  packaged installer comes later)

## Build and run

From PowerShell in the project folder:

```powershell
dotnet run --project src\SwitchYard.App
```

Or build once and start the executable directly:

```powershell
dotnet build -c Release
.\src\SwitchYard.App\bin\Release\net10.0\SwitchYard.exe
```

Pass `--data-dir <folder>` to use a separate, throwaway library.

Data lives in `%LOCALAPPDATA%\SwitchYard\`:
- `library.sqlite3`: library, folders, emulator profile, last selection
- `logs\last_launch.log`: emulator output from the most recent launch

**Library → Open Data Folder** opens it. The database format is unchanged from the
earlier Python build, so existing libraries open as they are.

## Project layout

| Path | What it is |
|---|---|
| `src/SwitchYard.Core` | Database, folder scanner, emulator launcher. No UI code. |
| `src/SwitchYard.App` | Avalonia desktop app: views, view models, generated placeholder covers |
| `tests/SwitchYard.Tests` | xUnit tests, including headless UI tests |
| `tests/FakeEmulator` | Stand-in emulator used by the launch tests |

## Automated checks

```powershell
dotnet test
```

The tests use placeholder files and the fake emulator. They never touch real games or a
real emulator. Set `SWITCHYARD_SCREENSHOTS` to a folder to also save PNGs of each window.

| Area | Tests |
|---|---|
| Scan preview | classification, update/DLC/duplicate flags, ambiguous names → Unclassified, filename/folder generation conflict, files left byte-for-byte untouched |
| Repeat scans | no duplicates, record IDs and user edits preserved |
| Persistence | library, profile and last selection survive a restart; reserved `console_title_id`/`igdb_id`; files stored separately from editions; path keys match the earlier build |
| Paths | spaces in game, emulator and working-directory paths pass through as single arguments |
| Failures | missing executable, missing game, missing `{game}` argument, an `.exe` that can't start, a second launch while running |
| Launch | fake process starts, exits, reports its exit code, and writes the launch log; Play returns to the same cover |
| UI | missing-file state, Play without a profile, all windows render |

## How it works (Milestone 1)

- **Add Folder…** scans recursively for `.nsp`, `.xci`, `.nsz`, `.xcz` and shows a preview.
  Choose a generation for the folder, correct individual rows, and edit titles. Suspected
  updates, DLC and duplicates are flagged and unchecked by default. Ambiguous filenames
  import as *Unclassified*. Files already in the library are recognized, and their records
  stay as they are.
- **Editions** have stable IDs. Launch paths are stored in a separate `edition_files`
  table (one file per edition for now, more later). Generation belongs to the edition, not
  to the emulator profile.
- **Emulator Settings…** stores executable, working directory (blank means the exe's
  folder), supported generation, and an argument list with one argument per line. `{game}`
  is replaced by the game path. **Use Eden preset** sets `-g` / `{game}` (verified on Eden
  v0.2.1). **Test Launch** tries the unsaved settings on the selected game.
- **Play** (or Enter / double-click) checks the paths, then starts the emulator with the
  program and each argument passed separately, so there's no shell quoting. When the
  emulator exits, the cover you launched is selected again and gets focus.

## Windows smoke test (real Eden)

Run by hand with your Eden install and game folder. Record results below.

1. Import a small folder that includes a path with spaces and one Arcade Archives title.
   Check the preview classifications. Run Add Folder on the same folder again and confirm
   everything shows "Already in library".
2. Close and reopen Switch Yard. Confirm the games, the Eden profile, and the selected
   cover are still there.
3. Launch a known working title. Exit Eden. Confirm Switch Yard comes back with the same
   cover selected and focused.
4. Rename or move one game file, and separately point the profile at a nonexistent exe.
   Confirm the app names what to fix (FILE MISSING / Locate File…, Emulator Settings…)
   and stays usable.

### Results

| Date | Build | Eden version | Step | Result | Notes |
|---|---|---|---|---|---|
| 2026-11-13 | Python | v0.2.1 | 1 Import + repeat import | Pass | |
| 2026-11-13 | Python | v0.2.1 | 2 Restart persistence | Pass | |
| 2026-11-13 | Python | v0.2.1 | 3 Launch, exit, focus return | Pass | Eden preset (`-g {game}`) booted the game directly |
| 2026-11-13 | Python | v0.2.1 | 4 Moved game / missing exe | Pass | |
| | C# | | 1 Import + repeat import | | |
| | C# | | 2 Restart persistence | | |
| | C# | | 3 Launch, exit, focus return | | |
| | C# | | 4 Moved game / missing exe | | |
