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
| Carousel | the centred cover is 15–20% larger than its neighbours and sits in the middle; switching views keeps the selection; carousel view is full screen and grid view restores the window |
| Playing | Eden games start in full screen (setting remembered, never added twice, other emulators untouched); one- or two-press exit with a 3-second window; closing a running emulator; hidden games persist, leave their library and counts, show under Hidden games and can be unhidden; schema v6 |
| Versions | add, remove and choose the main file; Play Version… launches the chosen file once; duplicates can be attached at import; Switch 2 editions are never matched to Switch 1 titles |
| Controller | buttons fire once, directions repeat when held, stick deadzone, held buttons ignored on return; D-pad moves the carousel, LB/RB switch library, Y switches view |
| IGDB (fake service) | one sign-in reused, credentials sent with every request, expired token renewed once, clear message for bad credentials, pasted links/slugs/IDs parsed, search text can't break the query |
| Matching | platform-aware by generation (both for Unclassified); confident matches only (same title, platform as tie-breaker); match-all summary, never re-matches, stops on bad credentials, can be cancelled |
| Metadata | description, genres, year, IGDB ID and cached artwork saved; refresh keeps the user's text and images; remove match keeps user edits; credentials stored locally only |
| Logos and backgrounds | IGDB logos used first (transparent preferred, PNG), work without SteamGridDB, fall back to SteamGridDB when IGDB has none or lacks the endpoint; SteamGridDB: exact-title game match (verified first), official logos ranked first, bad key and foreign image hosts refused; logo fetched once and the user's logo always wins; a new match drops the old game's logo; background choices from IGDB artworks, screenshots and SteamGridDB heroes, kept on refresh; choosers load once; schema v5 upgrade; selector covers have no generation banner |
| Metadata UI | Needs IGDB match filter; Match on IGDB… with previews, link look-up and Change Match; all-platforms search; edit description and set/reset cover; IGDB Settings test connection; artwork renders in grid, carousel and dialogs |
| TitleDB (fake server) | download saved once and skipped when unchanged; lookup by title ID prefers the full listing over demos; updates map to their base game; text, icon, banner and screenshots saved; images only from Nintendo's image server; a failed image is counted and retried next time; works offline from the saved copy; yours → TitleDB → IGDB display order; Not in TitleDB filter; offered once after the first import; schema v7 upgrade |
| Fixing a game | Clear Match and Artwork removes both matches, all descriptions and pictures and the artwork folder but keeps the sort title, and can look the game up again; Refresh from TitleDB re-downloads its pictures and says when a title ID isn't listed; the carousel right-click menu has no Play, Launch With, Play Version or Add Version File; SteamGridDB images are downloaded without the API key (the image server refuses it) |
| Browsing | sort titles order games and persist (blank or the title clears it); schema v8 upgrade; SEGA AGES by title prefix, in Arcade Archives only when ticked, with its own series filter; background setting (artwork, colour from the cover, plain) remembered; cover colour follows the artwork and is toned down for readable text; carousel covers and details band keep their size and place while browsing |
| Logos | each game's logo is fetched right after it is matched; Get Missing Logos only visits matched games without one, reports added / not found / failed, and can be cancelled |
| Large libraries | grid and carousel share one view across libraries; artwork comes from a background cache (covers, backgrounds, logos) and is re-decoded when the file changes; artwork can be replaced while the cache reads it; carousel centring with virtualization; opt-in 863-game stress test |
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
  with its own search (Ctrl+F), generation filter and last selection. Grid or carousel is
  one setting shared by both libraries. Arcade Archives also filters by series.
- **Arcade Archives** and **Arcade Archives 2** titles are placed by their title prefix.
  **ACA NEOGEO** and **SEGA AGES** titles stay in Main Library unless you tick *Include ACA
  NEOGEO* or *Include SEGA AGES* (shown in the Arcade Archives library); each then gets its
  own entry in the series filter.
  The **Library** dropdown in the details panel pins any game to a library; *Automatic*
  goes back to placement by title. Pins are stored apart from game records
  (`edition_library_overrides`).
- **Grid or carousel** (Ctrl+G, or the button at the top right): the carousel fills the
  screen and shows one row of covers just above the game's details, with the selected cover
  in the centre, 18% larger than its neighbours. Switching back to the grid leaves full screen. Both views
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
  | X | Game Actions menu (Launch With…, Play Version…, artwork, …) |
  | Start | Play |
  | Start + Select (Back) | while a game runs: close it and return to Switch Yard |

  Other input is ignored while another app, such as the emulator, is in front.

- **Playing** (Settings → **Playing**):
  - *Start Games in Full Screen* (on by default) adds Eden's full-screen switch (`-f`) at
    launch. For other emulators, add their own switch to the profile's arguments.
  - **Esc** (while the emulator is in front) or **Start + Select** on the controller closes
    the game the way the emulator's close button would, so it shuts down cleanly; if it
    hasn't closed after 5 seconds (for example, it's asking "are you sure?") it is stopped.
    Choose *Press Once to Exit* or *Press Twice to Exit* (the default: the first press shows
    a note over the game, and a second press within 3 seconds closes it).
- **Sort Title…** (Game Actions or right-click): sort a game under another name without
  renaming it, for example "Zelda 1", "Zelda 2" to keep a series in order. Clear it (or set
  it back to the title) to sort by the title again.
- **Background** (Settings → **Background**): *Game Artwork* (the banner or your chosen
  background), *Color from Game* (a gradient in the main colour of the selected cover), or
  *Plain*.
- **Full screen and focus:** going full screen, returning from a game and starting in the
  carousel all bring Switch Yard to the front, so the controller works without clicking. If
  Windows still leaves nothing in front, any controller button brings it back.
- **Hide Game** (Game Actions or right-click): hides a game you don't play from its library
  and counts without removing anything. **More filters → Hidden games** lists them; choose
  **Unhide Game** there to bring one back.

### Milestone 4: IGDB metadata and artwork

**One-time setup.** IGDB requires free Twitch developer credentials, and each user brings
their own, so no shared secret ships with Switch Yard:

1. Sign in at the [Twitch developer console](https://dev.twitch.tv/console/apps) and click
   **Register Your Application**. Use any name, OAuth redirect `http://localhost`, category
   *Application Integration*, client type *Confidential*.
2. Open the application, copy the **Client ID**, and click **New Secret** to get a
   **Client Secret**.
3. In Switch Yard: **Settings → Metadata Settings…**, paste both, click **Test IGDB**,
   then **Save**.

**Optional: more logos.** Logos come from IGDB first (its `logos` endpoint). For games
IGDB has no logo for, and for extra wide backgrounds, add
[SteamGridDB](https://www.steamgriddb.com/), a community artwork site: sign in (free),
open *Preferences → API*, generate an API key, and paste it into the SteamGridDB section of
**Metadata Settings…** (**Test SteamGridDB**, then **Save**).

Keys are stored only in `data\library.sqlite3` on this computer, never in the app or the
repository.

**Using it**

- **Settings → Match Unmatched Games** searches IGDB for every unmatched game, limited to
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
  a key. Each game's logo is fetched right after it's matched, so stopping *Match Unmatched
  Games* early keeps every logo found so far. **Get Missing Logos** fills in logos for games
  matched earlier, shows its progress, can be cancelled from the same menu, and ends with
  how many were added, not found or failed. The
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

### TitleDB: names, descriptions and square icons

- [TitleDB](https://github.com/blawar/titledb) is the eShop's own listing of every game
  (MIT-licensed data). Games are matched **exactly by their title ID**, which Switch Yard
  already reads from the file names, so there's nothing to search and no account or key.
- **Settings → Update from TitleDB** downloads the English (US) file once, about 90 MB, into
  `data	itledb\`, then fills in every game it lists: name, description, publisher, release
  date, genres, number of players, the square eShop icon (used as the cover) and the banner
  (used as the background). Running it again only downloads TitleDB if there's a newer copy
  and only fetches images that are missing. It can be cancelled from the same menu.
- After the first import, Switch Yard offers to do this; once TitleDB is downloaded, newly
  imported games are filled in automatically.
- **Choose Background…** lists the eShop banner and screenshots first.
- **Fixing one game** (Game Actions or right-click):
  - **Refresh from TitleDB** downloads its text, icon and banner again, replacing stale or
    broken downloads. Your own description and pictures stay.
  - **Clear Match and Artwork…** starts it over: its TitleDB and IGDB matches, descriptions
    and every picture (including ones you chose) are removed and its artwork folder deleted.
    Its files, versions, sort title and settings stay. Then look it up in TitleDB again, or
    leave it bare and match it on IGDB yourself.
- IGDB and SteamGridDB stay as backups: logos come from them, and games TitleDB doesn't list
  (**More filters → Not in TitleDB**) can still be matched on IGDB. Your own description and
  pictures always come first, then TitleDB, then IGDB.
- Covers are square in the grid, carousel and details panel to suit the eShop icons; IGDB's
  portrait covers are trimmed to fit when a game has no icon.

### Large libraries

Switch Yard is tuned for libraries of hundreds of games:

- **Artwork is decoded in the background and kept in memory.** Covers near the selection
  come first, then every cover in the library, so scrolling and browsing never wait for
  artwork. Backgrounds and logos for the games either side of the selection are preloaded
  too. Covers are decoded at the size they're drawn (scaled to your display), and the least
  recently used are dropped if memory runs short.
- The carousel and the import preview only create controls for what's on screen.
- The database uses write-ahead logging and imports in a single transaction.

Measured with 863 folders (base game, update and DLC in each; 2,877 files) on a 100%-scaled
display:

| | Result |
|---|---|
| Scan 2,877 files | 0.5 s |
| Import preview opens | under 1 s |
| Import 863 games | 0.1 s |
| Launch to first frame | about 1.8 s |
| All 863 covers in memory | about 0.25 s after the window appears (about 300 MB) |
| Grid smooth scroll / carousel (one game per frame) | 60 fps |

To repeat the measurements:

```powershell
# Builds a library shaped like the one above in %TEMP%\SwitchYard Stress and times each step
$env:SWITCHYARD_STRESS = "1"; dotnet test -c Release --filter "FullyQualifiedName~StressTests" --logger "console;verbosity=detailed"
# Real frame times with your graphics card (opens a window for about 10 seconds), written to logs\benchmark.txt
.\src\SwitchYard.App\bin\Release\net10.0\SwitchYard.exe --data-dir "$env:TEMP\SwitchYard Stress\run" --benchmark
```

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
   centred. Pressing buttons while the emulator is in front must not affect Switch Yard,
   except Start + Select.
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
13. Switch to the carousel (Switch Yard goes full screen) and back to the grid (it leaves
    full screen). Play a game: Eden opens in full screen. Press Esc twice to close it and
    come back; play again and use Start + Select. Switch to *Press Once to Exit* and repeat.
14. Hide a game, find it under **More filters → Hidden games**, and unhide it.
15. Run **Update from TitleDB**. Covers become square eShop icons, banners appear behind the
    carousel, and descriptions, publisher and players show in the details. Check
    **More filters → Not in TitleDB** and run it again (it should finish quickly).
16. Switch to the carousel and use the controller straight away (no click). Play a game,
    exit it, and use the controller again without clicking.
17. Try each **Background** option, set a **Sort Title** on a game, and tick *Include SEGA
    AGES* in Arcade Archives.

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
| | C# | | 13 Full screen and leaving a game | | |
| | C# | | 14 Hide and unhide | | |
| | C# | | 15 TitleDB and square covers | | |
| | C# | | 16 Focus without clicking | | |
| | C# | | 17 Background, sort title, SEGA AGES | | |
