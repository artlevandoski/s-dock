# Testing S-Dock

## Automated tests

```powershell
dotnet test
```

The tests use placeholder game files, a stand-in emulator (`tests/FakeEmulator`) and fake
versions of IGDB, SteamGridDB and TitleDB. They never touch real games, a real emulator or
the real services. Set `SDOCK_SCREENSHOTS` to a folder to also save PNGs of each window.

| Area | What's covered |
|---|---|
| Importing | file classification; update, DLC and duplicate flags; ambiguous names → Unclassified; filename/folder generation conflicts; repeat scans add nothing and keep edits; game files left byte-for-byte untouched; a file's own title ID is the last one in its name |
| Library storage | library, profiles and selection survive a restart; files stored apart from games; every schema upgrade (v1 → v9) keeps existing data |
| Launching | paths with spaces passed as single arguments; missing executable, game or `{game}` argument; an `.exe` that can't start; a second launch while one runs; exit code and launch log; Play returns to the same cover |
| Emulator choice | game's choice → generation default → ask; Unclassified always asks; remember for the game or as default; Launch With… saves nothing; two builds side by side; removing a profile clears choices that used it; Emulators folder discovery |
| PC versions and No emulator | .exe games always run on their own, even with no emulator set up; No emulator as a game's choice or the Switch 2 default (Switch 1 unaffected), remembered, and refused for a file that isn't a program; Add Game (.exe) with a suggested name, never added twice; title IDs from bracketed names; Set Title ID checks the format |
| Playing | Eden starts in full screen (setting remembered, never added twice, other emulators untouched); one- or two-press exit within 3 seconds; closing a running emulator |
| Emulator choice | grid dropdown keeps showing the emulator you pick and saves it; carousel X → Emulator page with the current choice ticked, applies at once, B back shows it; carousel emulator button opens the page |
| Updates | on start: checked quietly, asked Update now / Later, only downloaded and applied on Update now, never during a game; switched off in Settings → About is remembered; by hand: up to date, offline and "not the released folder" messages; a real update in a temp folder (`--update-now` with SDOCK_UPDATE_SOURCE) replaces `current` and keeps `data` and `emulators`, nothing written outside the folder |
| On-screen keyboard | D-pad and A type (one capital after Y), X space, B deletes then closes, LB/RB move the cursor, Select symbols, Start done; shown in the text editor when the controller was used last (not after typing or a click); Start saves; grid search opens with Up from the filters and filters as you type |
| Trailers | main "Trailer" preferred over other videos; saved with an IGDB match; found by name for other games without changing the match; "none" remembered for 30 days; no IGDB keys or no WebView2 explained; A/←→/↑↓ go to the player, B closes; Watch Trailer in both Game Actions menus; real player check: `S-Dock.exe --data-dir <temp> --trailer-test <YouTube id> <png>` opens, plays, closes silently and reopens inside the main window |
| Leaving a game | a close request from outside (Alt+F4 from Steam Input) within 4 s of an exit press is ignored and logged; S-Dock's own Quit still closes; data\logs\events.log records starts, exits, presses and closes |
| Grid with a controller | Up/Down straight along the column, to the last cover when the row below is shorter; Up from the top row into the filters; Left/Right between pills, More filters, series and Clear; A chooses or opens a dropdown (D-pad inside, A closes); Down or B back to the covers |
| Rename | name shown, sorted by and searched with; the EmuReady result is looked up again |
| Play history | sessions add up (under 30 s ignored, over 24 h capped); Recently Played tab most recent first, no letter jumps; "Played 12 h · last played Tuesday" line and carousel tag; a real launch is recorded; opens on the last game played (in its own library) or, if set, the last selected; Clear Play History asks first |
| Remove from Library | asks first; removes the game, its favorite, emulator choice, ratings and artwork folder; the game file stays; selection moves to the neighbour; cancel keeps everything |
| Move Game Folder | planned by relative path, then by unique file name (a name found twice isn't guessed); nothing changes until applied; games keep favorites and sort titles; duplicate entries from a later Add Folder merge into the originals; the folder record moves; files not found move along as missing; Settings shows how many games are missing; cancel and "nothing found" change nothing |
| Libraries and browsing | Arcade Archives, ACA NEOGEO and SEGA AGES by title prefix; manual pins and options persist; per-library search, filters and selection; hide and unhide; favorites (☆ button, Game Actions; star on covers; Favorites tab from every library); sort titles; leading A / An / The ignored when sorting; Update available, Not in TitleDB and Hidden filters |
| Grid and carousel | one shared view; the centred cover is larger and stays centred; carousel is full screen and the grid opens and comes back maximized; S-Dock opens in the launch view; covers and the details band keep their size and place while browsing; right-click menu differs by view |
| Settings page | opens the same in grid and carousel; Up/Down, A/Right in, Left/Right change a choice, B back one step then closed; switches, choices and actions; default emulators chosen on the page; dialogs return to the same setting; keyboard (arrows, Enter, Esc) without moving the games underneath; mouse clicks on sections, arrows and Close; Game Info shows status |
| Menus | settings apply at once and the tick follows; actions close the menu then run; B goes back a page or closes |
| Background | artwork, colour from the cover (toned down for readable text) or plain; remembered |
| Versions | add, remove and choose the main file; Play Version… launches once; duplicates attached at import; Switch 2 editions never matched to Switch 1 titles |
| Controller types | SDL: face buttons by label (Switch Pro A on the right confirms, PlayStation ✕ confirms, unknown labels by position); Start/Back/shoulders/D-pad; stick Y flipped to up-positive; triggers scaled; several controllers combined so a duplicate presses once; Settings lists the connected controllers |
| Controller | buttons fire once; directions repeat when held; stick deadzone; held buttons ignored on return; triggers past halfway and Select map to letter jumps and Settings; the Settings and Game Actions menus, their pages, ticks and actions, right-click, and letter jumps all driven through the controller path |
| Dialogs with a controller | questions (D-pad between buttons, A answers, B closes), Choose Cover (D-pad picks, A uses), text editor (A saves, D-pad to Save/Cancel), emulator picker, forms with dropdowns and check boxes; from the X menu into a dialog and back |
| Carousel text | first sentence (abbreviations, decimals, ellipses, quotes and taglines handled; long ones cut at a word); your own text replaces it and clearing it goes back |
| TitleDB | US listing first, then UK, Australian and Japanese (no Japanese text); AeroFoil's fork with the original as fallback; unchanged listings not downloaded again; demos never win over the full listing; images only from Nintendo's image server; failed images counted and retried; works offline from the saved copy; offered after the first import |
| EmuReady | on by default (switched off: nothing shown or asked, and remembered); tests never go online; matched by name on the right console (™, curly apostrophes, punctuation ignored; unsure matches skipped); typical rating per emulator, Windows reports first, handhelds as the fallback; the game's own emulator headlined; kept in the library and asked again after a week; offline shows nothing and keeps nothing |
| nx-versions | latest update and version dates; first-seen dates fill a missing release year; a failed refresh keeps the saved copy; turning it off stops downloads and keeps recorded versions; update and DLC files read from the game folder |
| IGDB | one sign-in reused; expired token renewed once; clear message for bad credentials; pasted links, slugs and IDs; platform-aware matching with confident matches only; match-all summary and cancel |
| Artwork | your choices always win, then TitleDB, then IGDB; Choose Cover, Background and Logo (including a file from the PC and Use Default); logos fetched right after a match; Get Missing Logos progress, summary and cancel; SteamGridDB images downloaded without the API key; Clear Match and Artwork; Refresh from TitleDB |
| Large libraries | background artwork cache, re-decoded when a file changes and replaceable while in use; carousel centring with virtualization; opt-in 863-game stress test |
| Renaming | old Switch Yard environment variables, profile folder and solution name still recognised |

## Manual checklist (real emulator)

Run by hand with your own emulator and game folder, and record results below.

**Release zip (each beta)**

0. Unzip `S-Dock-<version>-win-x64.zip` somewhere new and run `S-Dock\S-Dock.exe`: `data` appears
   inside the S-Dock folder and nothing new appears in `%LOCALAPPDATA%`. Copy a `data` and
   `emulators` folder in and check the library appears. Unzipped under `C:\Program Files`, S-Dock
   asks to be moved.

**Library and launching**

1. Import a small folder that includes a path with spaces and an Arcade Archives title.
   Check the preview, then import the same folder again: everything shows "Already in
   library".
2. Restart S-Dock. The games, emulator profile and selected game are still there.
3. Play a game and exit the emulator. S-Dock comes back with the same game selected.
4. Move one game file, and point the profile at a missing exe. S-Dock says what to fix
   (FILE MISSING / Locate File…, Emulator Settings…) and stays usable.

**Emulators**

5. Put one or two builds in `emulators\`, add them with **Add as profile**, set the Switch 1
   default, then give one game a different emulator and check Play uses it.
6. **Launch With…** another emulator; afterwards the game's own choice is unchanged.

**Browsing and controller**

7. Arcade Archives titles appear in their tab; each library keeps its own search and
   selection after a restart. Tick *Include SEGA AGES* and *Include ACA NEOGEO*.
8. With a PlayStation and a Switch Pro controller too (if you have them): A / ✕ chooses, B / ○ goes back,
   Share / − opens Settings, and both work in Eden afterwards as before. Settings → Playing →
   Controllers lists them.
8. Switch to the carousel (full screen) and use the controller straight away, without
   clicking: D-pad, A to play, LB/RB, X (Game Actions), Select (Settings), Y, Up/Down and
   LT/RT to jump letters. Choose things in both menus with A, go back with B. Right-click a
   cover with the mouse: the same Game Actions menu opens. Back in the grid, the window is
   restored.
9. Play from the carousel and exit with Esc (twice), then with Start + Select. Use the
   controller again without clicking. Try *Press Once to Exit*.
10. Try each **Background** option. Set a **Sort Title**. Hide a game and unhide it.
11. Add a second file to a game (Add Version File…) and launch it with Play Version….

**Game info and artwork**

12. Run **Update from TitleDB**: square covers, banners, descriptions, publisher and
    players appear. Run it again (it finishes quickly). Check **More filters → Not in
    TitleDB** and **Update available**.
13. Enter IGDB credentials (Test IGDB succeeds), run **Match unmatched games on IGDB**, and
    fix one game with **Match on IGDB…** using a pasted igdb.com link.
14. Add a SteamGridDB key and run **Get Missing Logos**. Logos replace the names.
15. Use **Choose Cover…** (including a file from your PC), **Choose Background…** and
    **Choose Logo…**, then **Use Default**. Edit a description, then **Refresh from IGDB**:
    your description and pictures stay.
16. On one game, use **Clear Match and Artwork… → Clear and Look Up Again**.
17. Move the whole S-Dock folder (or go offline): artwork and emulators still work.

### Results

| Date | Build | Emulator | Steps | Result | Notes |
|---|---|---|---|---|---|
| 2026-11-13 | Python | Eden v0.2.1 | 1–4 | Pass | Eden preset (`-g {game}`) booted the game directly |

## Performance

S-Dock is tuned for libraries of hundreds of games:

- Artwork is decoded in the background and kept in memory: covers near the selection first,
  then every cover, so scrolling never waits. Backgrounds and logos either side of the
  selection are preloaded. Covers are decoded at the size they're drawn, and the least
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
| TitleDB: build merged file / look up a library | about 5 s / about 0.5 s |
| Read update and DLC files beside 862 games | about 1.4 s |

To repeat the measurements:

```powershell
# Builds a library shaped like the one above in %TEMP%\S-Dock Stress and times each step
$env:SDOCK_STRESS = "1"; dotnet test -c Release --filter "FullyQualifiedName~StressTests" --logger "console;verbosity=detailed"
# Real frame times with your graphics card (opens a window for about 10 seconds), written to logs\benchmark.txt
.\src\SDock.App\bin\Release\net10.0\S-Dock.exe --data-dir "$env:TEMP\S-Dock Stress\run" --benchmark
```
