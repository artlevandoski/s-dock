# Switch Yard

An independent Windows frontend for certain game libraries. You bring your own game files and
your own emulator installation. Switch Yard does not include or download games, firmware,
keys, or system files.

**Status:** Milestone 4. Browse Main Library and Arcade Archives in a grid or a
controller-friendly carousel with artwork and descriptions from IGDB, set up several
emulator profiles, and keep several versions of a game. Built with C# / .NET 10 and
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
| Libraries | Arcade Archives / Arcade Archives 2 / ACA NEOGEO by title prefix; manual pins and the NEOGEO option persist; per-library search, filters, view and selection survive switching and restart; returning from the emulator reopens the launched game's library |
| Carousel | the centred cover is 15–20% larger than its neighbours and sits in the middle; switching views keeps the selection |
| Versions | add, remove and choose the main file; Play Version… launches the chosen file once; duplicates can be attached at import; Switch 2 editions are never matched to Switch 1 titles |
| Controller | buttons fire once, directions repeat when held, stick deadzone, held buttons ignored on return; D-pad moves the carousel, LB/RB switch library, Y switches view |
| IGDB (fake service) | one sign-in reused, credentials sent with every request, expired token renewed once, clear message for bad credentials, pasted links/slugs/IDs parsed, search text can't break the query |
| Matching | platform-aware by generation (both for Unclassified); confident matches only (same title, platform as tie-breaker); match-all summary, never re-matches, stops on bad credentials, can be cancelled |
| Metadata | description, genres, year, IGDB ID and cached artwork saved; refresh keeps the user's text and images; remove match keeps user edits; credentials stored locally only |
| Logos and backgrounds | IGDB logos used first (transparent preferred, PNG), work without SteamGridDB, fall back to SteamGridDB when IGDB has none or lacks the endpoint; SteamGridDB: exact-title game match (verified first), official logos ranked first, bad key and foreign image hosts refused; logo fetched once and the user's logo always wins; a new match drops the old game's logo; background choices from IGDB artworks, screenshots and SteamGridDB heroes, kept on refresh; choosers load once; schema v5 upgrade; selector covers have no generation banner |
| Metadata UI | Needs IGDB match filter; Match on IGDB… with previews, link look-up and Change Match; all-platforms search; edit description and set/reset cover; IGDB Settings test connection; artwork renders in grid, carousel and dialogs |
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

### Milestone 3: grid, carousel and libraries

- **Libraries:** *Main Library* and *Arcade Archives* are separate tabs (Ctrl+Tab), each
  with its own search (Ctrl+F), generation filter, view and last selection. Arcade Archives
  also filters by series.
- **Arcade Archives** and **Arcade Archives 2** titles are placed by their title prefix.
  **ACA NEOGEO** titles stay in Main Library unless you tick *Include ACA NEOGEO*.
  The **Library** dropdown in the details panel pins any game to a library; *Automatic*
  goes back to placement by title. Pins are stored apart from game records
  (`edition_library_overrides`).
- **Grid or carousel** (Ctrl+G, or the button at the top right): the carousel shows one
  row with the selected cover in the centre, 18% larger than its neighbours. Both views
  share one selection. Covers in the grid and carousel show only the artwork, without a
  generation banner; the generation is in the details panel.
- **Versions:** a game can have several files (updates, revisions, alternate dumps).
  Attach one with **Add Version File…**, or tick *Add as a version of …* on a duplicate in
  the import preview. **Play Version…** launches another file once, makes it the main
  file, or removes it from the game. Files on disk are never touched.
- **Switch 2 editions** stay separate: duplicate detection only matches titles within the
  same generation.
- **Controller** (Xbox-compatible, through Windows XInput):

  | Button | Action |
  |---|---|
  | D-pad / left stick | move between covers (held: repeats) |
  | A | Play the selected cover; press the focused button in dialogs |
  | B | back / cancel (closes dialogs) |
  | LB / RB | previous / next library |
  | Y | grid / carousel |
  | X | Launch With… |
  | Start | Play |

  Input is ignored while another app, such as the emulator, is in front.

### Milestone 4: IGDB metadata and artwork

**One-time setup.** IGDB requires free Twitch developer credentials, and each user brings
their own, so no shared secret ships with Switch Yard:

1. Sign in at the [Twitch developer console](https://dev.twitch.tv/console/apps) and click
   **Register Your Application**. Use any name, OAuth redirect `http://localhost`, category
   *Application Integration*, client type *Confidential*.
2. Open the application, copy the **Client ID**, and click **New Secret** to get a
   **Client Secret**.
3. In Switch Yard: **Metadata → Metadata Settings…**, paste both, click **Test IGDB**,
   then **Save**.

**Optional: more logos.** Logos come from IGDB first (its `logos` endpoint). For games
IGDB has no logo for, and for extra wide backgrounds, add
[SteamGridDB](https://www.steamgriddb.com/), a community artwork site: sign in (free),
open *Preferences → API*, generate an API key, and paste it into the SteamGridDB section of
**Metadata Settings…** (**Test SteamGridDB**, then **Save**).

Keys are stored only in `data\library.sqlite3` on this computer, never in the app or the
repository.

**Using it**

- **Metadata → Match Unmatched Games** searches IGDB for every unmatched game, limited to
  the game's platform (Nintendo Switch or Nintendo Switch 2; both for Unclassified). Only
  clear matches are applied: same title (ignoring case, punctuation and ™), and if several
  games share it, the only one on the right platform. The rest show under the **Needs IGDB
  match** filter.
- **Match on IGDB…** (details panel → **Metadata**, or right-click a cover) shows an editable
  search with cover previews, year and platforms, a *Search all platforms* option, and a
  box for an IGDB link, slug or numeric ID. On a matched game it becomes **Change Match…**.
- The chosen IGDB ID is saved, and the cover and landscape key art are downloaded once into
  `data\artwork\`, so they travel with the portable folder and work offline.
- **Logos:** matching a game also downloads its best logo: IGDB's first (transparent ones
  preferred, saved as PNG), otherwise SteamGridDB's (official style first) if you've added
  a key. **Metadata → Get Missing Logos** fills in logos for games matched earlier. The
  logo replaces the game name under the carousel and in the details panel.
- **Choose Background…** (details panel → **Metadata**, or right-click a cover) shows the
  game's IGDB artworks and screenshots plus SteamGridDB heroes. The one you pick shows
  behind the carousel.
- **Choose Logo…** lists the game's IGDB logos, then its SteamGridDB logos. Both choosers
  also offer **From File…** and **Use Default**.
- **Edit Description…**, **Set Cover Image…** and your picks in the choosers are stored
  separately from downloaded data. **Refresh from IGDB** updates IGDB's data but never
  replaces yours; **Use Default Artwork** removes your images.
- Videos are planned for later.

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
7. Import a folder with an Arcade Archives title. Confirm it appears under *Arcade
   Archives*, switch libraries, and check each keeps its own search, view and selection
   after a restart.
8. In the carousel, browse with the arrow keys, then with a controller (D-pad, A to play,
   LB/RB, Y). Launch from the carousel and exit the emulator; the same cover comes back
   centred. Pressing buttons while the emulator is in front must not affect Switch Yard.
9. Add a second file to a game (Add Version File…), then use Play Version… to launch it.
10. Enter your IGDB credentials (Test Connection must succeed), run **Match Unmatched
    Games**, and check covers, descriptions and genres. Use **Needs IGDB match** and fix
    one game with **Match on IGDB…** (try a pasted igdb.com link).
11. Edit a description and set your own cover, then **Refresh from IGDB**: both must stay.
    Move the whole folder (or restart offline) and confirm the artwork still shows.
12. Run **Get Missing Logos** and check the IGDB logo replaces the name in the carousel and
    details panel. Optionally add a SteamGridDB key and run it again for games IGDB had no
    logo for. Use **Choose Background…** and **Choose Logo…** to pick others, then **Use
    Default** to go back.

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
| | C# | | 7 Libraries and per-library state | | |
| | C# | | 8 Carousel, keyboard and controller | | |
| | C# | | 9 Versions and Play Version… | | |
| | C# | | 10 IGDB setup and matching | | |
| | C# | | 11 User edits kept, offline artwork | | |
| | C# | | 12 Clear logos and Choose Background | | |
