# Switch Yard

An independent Windows frontend for certain game libraries. You bring your own game files and
your own emulator installation. Switch Yard does not include or download games, firmware,
keys, or system files.

**Status:** Milestone 2. Import a folder, browse the cover grid, set up several emulator
profiles, and choose which one each game launches with. Built with C# / .NET 10 and
[Avalonia](https://avaloniaui.net/).

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

## Portable by default

Switch Yard keeps its own files beside itself, so the whole folder can be copied to another
PC or a USB drive:

```
SwitchYard\
  SwitchYard.exe
  data\        library.sqlite3 (library, profiles, defaults, last selection) and logs\
  emulators\   emulator builds you place here
```

- Running from source, the repository is that folder: `data\` and `emulators\` sit at the
  repo root and are git-ignored.
- Emulator paths inside the folder (such as `emulators\Eden-Windows\eden.exe`) are stored
  relative to it, so they keep working after the folder moves or gets a new drive letter.
  Game folders and emulators elsewhere are stored as full paths.
- If the folder can't be written to (for example under `C:\Program Files`), data goes to
  `%LOCALAPPDATA%\SwitchYard` instead.
- On the first portable start, a library from an earlier build in
  `%LOCALAPPDATA%\SwitchYard` is copied into `data\`. The original stays where it was as
  a backup.

**Library → Open Data Folder** opens the data folder in use; its tooltip shows the path.

### Portable build

Packages the .NET runtime, so it runs on PCs without .NET installed:

```powershell
dotnet publish src\SwitchYard.App -c Release -r win-x64 --self-contained -o publish\SwitchYard
```

Copy `publish\SwitchYard` wherever you like. It is about 210 MB unzipped.

## Project layout

| Path | What it is |
|---|---|
| `src/SwitchYard.Core` | Database, folder scanner, emulator launcher. No UI code. |
| `src/SwitchYard.App` | Avalonia desktop app: views, view models, generated placeholder covers |
| `tests/SwitchYard.Tests` | xUnit tests, including headless UI tests |
| `tests/FakeEmulator` | Stand-in emulator used by the launch tests |
| `emulators/` | Your local emulator builds. Git-ignored except its README. |
| `data/` | Your library and logs when run from source. Git-ignored. |

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
| Launch routing | game's choice → generation default → ask; Unclassified games always ask; picker can remember for the game or as default; Launch With… saves nothing; two builds of the same emulator side by side |
| Profiles | add, duplicate, remove; removing a profile clears games and defaults that used it; Emulators folder discovery skips uninstallers and helpers; unknown emulators get no unverified preset |
| Portable | data and emulators beside the app; installed copies keep data outside the replaceable folder; read-only folders fall back to the user profile; old library copied once and kept; emulator paths survive the folder moving |
| Upgrade | a Milestone 1 library with one profile makes it the Switch 1 default; several profiles set no default |
| UI | missing-file state, Play without a profile, details dropdown, all windows render |

## How it works

### Milestone 1: library and launching

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

### Milestone 2: emulator profiles and launch routing

- **Emulator Settings…** lists named profiles (Add, Duplicate, Remove), each with its own
  executable, working directory, supported generation, arguments and Test Launch. Two
  builds of the same emulator can sit side by side, such as "Eden 0.2.1" and "Eden nightly".
- **Emulators folder:** builds placed in `emulators/` show up in Emulator Settings; **Add
  as profile** names the profile after the build's folder. Eden builds get the Eden preset.
  Other emulators get only `{game}` until their arguments are verified on a real Windows
  build.
- **Defaults:** choose a default profile for Switch 1 and for Switch 2.
- **Per-game choice:** the **Emulator** dropdown in the details panel sets "Use default"
  or a specific profile. The line above Play shows which profile it will use and why.
- **Play** uses the game's own choice, then its generation's default, and otherwise asks.
  The picker can remember the answer for the game or make it the generation default.
  Unclassified games have no generation default.
- **Launch With…** (details panel or right-click a cover) picks a profile for one launch
  and saves nothing.
- Emulator choices are stored apart from game records (`generation_defaults`,
  `edition_launch_overrides`). Removing a profile clears any choices that used it.

> Two builds of the same emulator may still share one settings and save folder (for Eden,
> `%APPDATA%\eden`) unless a build runs in portable mode. Separating them is part of the
> later Emulator Manager milestone.

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
5. Put an Eden build in `emulators/` (or two builds in separate folders). In Emulator
   Settings, add each with **Add as profile**, set the Switch 1 default, and save. Check
   the line above Play, then set one game's **Emulator** dropdown to the other profile and
   confirm Play uses it.
6. Use **Launch With…** on a game, pick a different profile, play, and exit. Confirm the
   game's Emulator dropdown hasn't changed.

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
| | C# | | 5 Two profiles, defaults, per-game choice | | |
| | C# | | 6 Launch With… | | |
