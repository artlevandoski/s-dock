# S-Dock

A controller-friendly Windows frontend for switch game libraries. Point it at your game
folders and your own emulator, and browse everything in a grid or a full-screen carousel
with covers, backgrounds, logos and descriptions.

You bring your own game files and emulator. S-Dock does not include or download games,
firmware, keys or system files, and it never changes your game files.

Built with C# / .NET 10 and [Avalonia](https://avaloniaui.net/). Stable, with automatic updates.

## Contents

- [Features](#features)
- [Install](#install)
- [Getting started](#getting-started)
- [Using S-Dock](#using-s-dock)
- [Game info and artwork](#game-info-and-artwork)
- [Controller and keyboard](#controller-and-keyboard)
- [Portable folder](#portable-folder)
- [For developers](#for-developers)

## Features

<!-- Keep this list complete: add every new feature here (one short line), with details further down. -->

**Browsing**
- Full-screen **carousel** with the game's artwork behind the covers, or a **grid** with a details
  panel; choose which one S-Dock opens in.
- Tabs: **Main Library**, **Arcade Archives** (sorted in automatically, ACA NEOGEO and SEGA AGES
  optional), **Favorites** and **Recently Played**. Any game can be moved to another tab by hand.
- **Favorites** with a gold star on the cover; **play time** and *last played* for every game; opens
  on the last game you played.
- **Search** in both views, **jump by letter**, and sorting that ignores a leading A, An or The.
- **Filters:** Switch 1, Switch 2, Unclassified, Update available, Not in TitleDB, Hidden games.
- Shows whether you have the latest **update** and how many **DLC**, from the files beside each game.

**Playing**
- An emulator per game or per generation, **Launch With…** for a one-off, or **no emulator** for
  PC versions and other `.exe` games.
- **Start + Select** (or Esc) closes the game and returns to S-Dock on the same game, even with
  Steam Input running.
- Games start in full screen, and S-Dock comes back to the front afterwards.

**Game Actions**
- **Watch Trailer** inside S-Dock, controlled with the controller.
- **Rename**, Sort Title, Edit Description and Edit Carousel Text.
- Choose cover, background or logo; add and play alternate version files.
- Hide a game, or **Remove from Library** (the game file is never touched).
- Re-match on TitleDB or IGDB, or clear a game's match and artwork.

**Game info and artwork**
- **TitleDB** (names, descriptions, covers, backgrounds, screenshots), **nx-versions** (latest
  updates), **IGDB** (backup info, logos, trailers), **SteamGridDB** (more artwork, optional) and
  **EmuReady** ratings (how well each game runs on PC emulators).

**Controller and keyboard**
- Xbox, PlayStation, Switch Pro, Joy-Con and most other controllers, with no drivers; several at once.
- Everything works with a controller: both views, filters, menus, dialogs and Settings.
- **On-screen keyboard** for search and every text box.
- Keyboard shortcuts and the mouse work throughout.

**Settings, portability and updates**
- A full-screen **Settings** page made for the controller; changes apply straight away.
- Background style: the game's artwork or a plain colour.
- **Portable:** everything stays in the S-Dock folder; move the folder to move S-Dock.
- **Move Game Folder** finds your games again after they move, keeping their settings and artwork.
- **Automatic updates** that keep your data and emulators.
- Logs, and S-Dock keeps running after an unexpected error.

## Install

S-Dock runs on 64-bit Windows 10 or 11, and there's nothing to install: it's one folder.

1. Download `S-Dock-<version>-win-x64.zip` and unzip it wherever you like: Documents, another
   drive, a USB stick. You get an **S-Dock** folder.
2. Run `S-Dock.exe` inside it. Put your emulators in its `emulators` folder.

Everything S-Dock saves (your library, artwork, TitleDB and settings) goes into the `data` folder
beside it, and nowhere else on your PC. To move S-Dock, move the folder; to remove it, delete the
folder. Don't put it under `C:\Program Files`: Windows doesn't let programs save there, and S-Dock
will ask you to move it.

S-Dock isn't code-signed yet, so Windows SmartScreen may say it "protected your PC": choose
**More info → Run anyway**.

**Updates.** From 1.0.0 on, S-Dock updates itself: when it starts it checks S-Dock's GitHub
releases (full releases, not pre-releases) and, if there's a newer version, asks **Update now / Later**. Updating downloads the new
version, closes S-Dock and starts it again. Only the app itself is replaced; `data` and
`emulators` stay as they are, and everything stays inside the S-Dock folder. Check by hand, or
switch off the check on start, in **Settings → About**. It never interrupts a game.

**Bringing an existing library across** (or updating from a 0.1.0 beta): close S-Dock, then copy the
`data` and `emulators` folders from your old S-Dock folder into the new one.

## Getting started

To run from source instead, you need the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0):

```powershell
dotnet run --project src\SDock.App
```

Then open **Settings** (the gear, or Select on a controller):

1. **Emulators → Emulator Profiles…** (Ctrl+E): add your emulator. Builds placed in the `emulators\`
   folder are found automatically; **Add as profile** sets them up. For Eden, **Use Eden
   preset** fills in the right arguments, and **Test Launch** checks it.
2. **Add Folder…** (Ctrl+O): pick a folder of game files (`.nsp`, `.xci`, `.nsz`, `.xcz`). A
   preview lets you set each game's generation and fix titles before importing. Updates,
   DLC and duplicates are left out by default.
3. When asked, let S-Dock download **TitleDB** to fill in names, descriptions and pictures.

**PC versions** (recompilations and ports that are their own `.exe`): use **Settings → Library → Add
Game (.exe)…**. They run on their own, without an emulator, and are filed as Switch 2 (change it in
the details panel). A title ID in brackets in the file or folder name, like `[0100…]`, lets
TitleDB fill them in; otherwise use **Game Info and Matching → Set Title ID…**.

## Using S-Dock

**Libraries.** *Main Library* and *Arcade Archives* are separate tabs, each with its own
search, filters and last selection. Arcade Archives and Arcade Archives 2 titles go there
automatically; turn on **Settings → Library → Include ACA NEOGEO / Include SEGA AGES** to add those
series too.
The **Library** dropdown in the details panel moves any game by hand.

**Favorites.** The *Favorites* tab gathers the games you've marked, from every library. Mark or
unmark a game with the **☆** button beside Play (grid and carousel), **Game Actions → Add to
Favorites**, or Ctrl+D. Favorite covers show a gold star.

**Recently Played and play time.** S-Dock notes when each game is played and for how long. The
*Recently Played* tab lists the games you've played, most recent first; each game shows a line like
*Played 12 h · last played Tuesday* (in the details panel, and as a tag in the carousel). Sessions
under 30 seconds don't count, so a game that fails to start isn't "played". S-Dock opens on the last
game you played; **Settings → Library → Start on** can make it the last game selected instead, and
**Clear Play History…** forgets it all.

**Grid or carousel.** The grid shows a details panel beside the covers and opens maximized. The carousel fills
the screen with one row of covers above the game's name and the first sentence of its
description (**Edit Carousel Text…** changes it); switching back to the grid leaves full
screen. Both views share the same selection, and S-Dock opens in the one chosen under
**Settings → Library → Launch view**. The carousel is kept clean: Add Folder, the filter bar and
the view buttons appear only in the grid (Y or Ctrl+G switches views, and filters are under
**Settings → Library → Filter** in either view). Search works in both: in the carousel press
**Up** (or Ctrl+F, or the search button by the gear); its search box shows only while you search or
while a search is filtering the covers.

**Jump by letter.** LT/RT on a controller, or Page Up/Page Down, jump
to the next or previous letter (by sort title). The letter shows briefly in the middle.
Games are sorted without a leading "A", "An" or "The", so *The Messenger* is under M.

**Filters.** All Games, Switch 1 and Switch 2, plus **More filters**: Unclassified,
Update available, Not in TitleDB and Hidden games. With a controller in the grid, press Up from
the top row to reach the filters: Left/Right move between them, A chooses (or opens More filters),
Down or B goes back to the covers. **Settings → Library → Filter** works in both views.

**Playing.** **Play** (A, Enter or double-click) uses the game's own emulator choice, then
its generation's default, and otherwise asks you. Choose a game's own emulator in either view with
**Game Actions → Emulator** (in the carousel also the emulator button beside Play), or in the grid's
**Game settings → Emulator** dropdown. **Launch With…** picks an emulator just once. When the game
closes, S-Dock comes back with the same game selected.

- **No emulator** is a choice in each game's emulator list, and for the Switch 2
  default in **Settings → Emulators**. It runs the game's own `.exe`. PC versions always run this
  way.

- **Esc** (while the game is in front) or **Start + Select** closes the game and returns to
  S-Dock. By default you press twice: the first press shows a note, and a second press within
  3 seconds closes it. Change this under **Settings → Playing**. S-Dock asks the emulator to close and
  stops it if it hasn't after 2.5 seconds; to skip Eden's "stop emulation?" question, set Eden's
  **Confirm before stopping emulation** to *Never*. Steam Input often sends Alt+F4 for Start + Select
  as well; S-Dock ignores close requests for a few seconds after an exit press, so only the game closes.
- **Start Games in Full Screen** (on by default) starts Eden in full screen. For other
  emulators, add their own full-screen switch to the profile.

**Game Actions** (the X button, the Game Actions button, or right-click a cover) open as a list in
the middle of the screen: move with the D-pad or arrow keys, choose with A or Enter, go back with B
or Esc. The carousel keeps its menu short: **Emulator**, **Watch Trailer**, **Add to Favorites**,
**Hide Game** and **More Game Actions (Grid View)**; the grid has everything below.

**Game Actions:**

| Action | What it does |
|---|---|
| Launch With… / Play Version… | Play once with another emulator, or another file of the game |
| Choose Cover / Background / Logo… | Pick from downloaded artwork or browse your PC; *Use Default* goes back |
| Watch Trailer | Play the game's trailer inside S-Dock (also a button in the grid's details panel). A pauses, Left/Right skip 10 s, Up/Down change the volume, B or Esc closes |
| Rename… | Change the game's name: the one shown, sorted by, and used to look it up online (EmuReady, IGDB). If a rating or match isn't found, try the name as that site lists it |
| Edit Description… / Edit Carousel Text… | Write your own description, or the one line shown in the carousel |
| Add Version File… | Attach another file (an update, revision or alternate dump) to the game |
| Sort Title… | Sort the game under another name, e.g. "Zelda 1", without renaming it (used exactly as typed) |
| Add to / Remove from Favorites | Mark the game as a favorite; it also shows in the **Favorites** tab |
| Emulator › | The game's own emulator: the default, any profile, or no emulator. The tick shows the current one; a choice applies at once |
| Hide Game | Hide it from its library; find it again under **More filters → Hidden games** |
| Remove from Library… | Take the game out of S-Dock with its artwork, description and settings (asks first). The game file itself stays on your PC; adding its folder again brings the game back |
| Game Info and Matching › | Refresh from TitleDB, **Set Title ID…**, match or refresh on IGDB, use the default artwork, or **Clear Match and Artwork…** to start the game over |

**Updates and DLC.** S-Dock reads the update and DLC files beside each game (for example in
`Updates` and `DLC` subfolders) from their file names. The details show whether you have
the latest known update and how many DLC you have.

**Settings** (Select, or the gear) is a full-screen page, the same in the grid and the carousel.
Sections are on the left (Library, Emulators, Playing, Appearance, Game Info, About) and their
settings on the right: switches, choices shown as `‹ value ›`, and buttons that open a dialog.

| Controller | Keyboard | Mouse | |
|---|---|---|---|
| Up / Down | Arrow keys | | Move between sections or settings |
| A or Right | Enter or Right | Click | Go into a section; flip a switch, run an action, move a choice on |
| Left / Right | Left / Right | Click `‹` `›` | Change a choice |
| B | Esc | **Close** | Back one step: from the settings to the sections, then out of Settings |
| Select | | | Close Settings |

Settings apply straight away. A dialog opened from Settings (such as Emulator Profiles) returns to
the same setting when you close it.

## Game info and artwork

S-Dock combines several sources. Your own choices always come first, then TitleDB, then
IGDB.

| Source | Gives | Setup |
|---|---|---|
| [TitleDB](https://github.com/blawar/titledb) | Name, description, publisher, release date, genres, players, square eShop icon (the cover), banner (the background), screenshots | None. Matched exactly by each file's title ID. |
| [nx-versions](https://github.com/16BitWonder/nx-versions) | Latest known update for each game, and dates | None. On by default (**Settings → Game Info → Use nx-versions**). |
| [IGDB](https://www.igdb.com/) | Backup for games TitleDB doesn't list, logos, and trailers | Free Twitch developer credentials (below) |
| [SteamGridDB](https://www.steamgriddb.com/) | More logos, covers and backgrounds | Free API key (optional) |
| [EmuReady](https://www.emuready.com/) | How well each game runs on PC emulators, from community reports | None. On by default (**Settings → Game Info → Show EmuReady ratings**). |

**Trailers.** **Watch Trailer** plays the game's YouTube trailer full screen inside S-Dock, using
Microsoft Edge WebView2 (part of Windows 10 and 11). Trailers come from IGDB, so they need your free
IGDB keys: a game matched on IGDB gets its trailer with the match, and for any other game S-Dock
asks IGDB when you first press Watch Trailer (without changing the game's match). If none is found,
renaming the game to IGDB's name usually helps.

**EmuReady.** With **Show EmuReady ratings** on (the default), the game you stop on is looked up on
emuready.com (at most once a week per game) and its rating shows under Play, e.g. *EmuReady: Great on
Eden · 9 PC reports*, plus a tag in the carousel. It uses the rating for the emulator the game plays
with, or the most-reported one, and Windows reports first. When there are no PC reports, handheld
reports are used and labelled as such. Click the rating in the grid to open the game on EmuReady.
Games are matched by name and console, so some aren't found; nothing is shown for those.

**TitleDB.** **Settings → Game Info → Update from TitleDB** downloads the US English listing, then the UK,
Australian and Japanese listings for games it lacks, and merges them into one file in
`data\titledb\` (about 360 MB to download, about 65 MB kept). Japanese entries provide only
pictures, names and dates. The listings come from AeroFoil's more frequently updated fork
([luketanti/titledb](https://github.com/luketanti/titledb)), with the original as a fallback.
Running it again downloads only what changed. Newly imported games are filled in
automatically.

**IGDB setup** (each user brings their own credentials; nothing secret ships with S-Dock):

1. Sign in at the [Twitch developer console](https://dev.twitch.tv/console/apps) and
   **Register Your Application**: any name, OAuth redirect `http://localhost`, category
   *Application Integration*, client type *Confidential*.
2. Copy the **Client ID**, and click **New Secret** for a **Client Secret**.
3. In S-Dock, **Settings → Game Info → IGDB and SteamGridDB keys…**: paste both, **Test IGDB**, then **Save**.

Then use **Settings → Game Info → Match unmatched games on IGDB** for games without info, or **Match on IGDB…**
(details panel → Metadata) for one game. You can search, paste an igdb.com link, or enter
an IGDB ID.

**SteamGridDB setup (optional):** sign in, open *Preferences → API*, generate a key, and
paste it into **IGDB and SteamGridDB keys…**. Then run **Get missing logos**.

Keys are stored only in `data\library.sqlite3` on your computer.

## Controller and keyboard

Xbox, PlayStation (DualShock 4, DualSense), Switch Pro, Joy-Con pairs and most other controllers
work, with no extra drivers, through [SDL](https://www.libsdl.org/) (the same library many emulators
use). Buttons go by their label: the button marked **A** (or **✕**) chooses and **B** (or **○**) goes
back, so on a Switch Pro controller A is the right-hand button. The table uses Xbox names: **Select**
is Share / Create on PlayStation and **−** on Switch; **Start** is Options or **+**. Several
controllers can be connected at once. **Settings → Playing → Controllers** shows which ones S-Dock
sees.

| Controller | Keyboard | Action |
|---|---|---|
| D-pad / left stick | Arrow keys | Move between covers (hold to repeat). In the grid, Up/Down go straight up or down a row (from the top row, up to the filters, then search); in the carousel, Up searches |
| A | Enter | Play, or choose in a menu |
| B | Esc | Back / close a menu or dialog |
| X | Right-click | Game Actions |
| | Ctrl+D | Add or remove a favorite |
| Select (Back) | | Settings |
| Y | Ctrl+G | Grid / carousel |
| LB / RB | Ctrl+Shift+Tab / Ctrl+Tab | Previous / next tab (Main Library, Arcade Archives, Favorites, Recently Played) |
| LT / RT | Page Up / Page Down | Previous / next letter |
| Start | | Play |
| Start + Select | Esc (in a game) | Close the game and return to S-Dock |
| | Ctrl+F | Search (in either view) |
| | Ctrl+O / Ctrl+E | Add Folder / Emulator Profiles |

In dialogs (questions, choosers, pickers, text editors, settings windows) the D-pad moves through
lists and then between buttons, A presses or chooses (it ticks check boxes and opens dropdowns),
and B cancels. Only Windows' own file and folder pickers (Add Folder, browsing for an image) still
need a keyboard and mouse.

**Typing with the controller.** An on-screen keyboard appears wherever you'd type: Rename, Sort
Title, Edit Description and Edit Carousel Text (it shows when you're using the controller; the
**Keyboard** button shows or hides it), and search (Up in the carousel; in the grid, Up from the filters). D-pad
moves, **A** types, **B** deletes (and closes once there's nothing left), **X** space, **Y** shift,
**LB/RB** move the cursor, **Select** symbols, **Start** done. Search results update as you type.

Controller input is ignored while another app is in front, except Start + Select during a
game. In full screen, S-Dock brings itself to the front when you return from a game.

## Portable folder

S-Dock keeps everything beside itself, so the whole folder can move to another PC or a USB
drive:

```
S-Dock\
  S-Dock.exe   starts S-Dock
  current\     the app itself (replaced by updates)
  Update.exe   applies updates
  data\        library database, artwork, TitleDB, logs, and the trailer player's cache (webview)
  emulators\   emulator builds you place here
  packages\    downloaded updates (appears after the first update)
```

- Emulators inside the folder are remembered relative to it, so they keep working after a
  move. Game folders elsewhere are remembered by their full path.
- **Games somewhere else now** (another PC, another drive, a tidied-up folder)? Use **Settings →
  Library → Move Game Folder…** (also offered on a missing game). Choose the folder as S-Dock knew
  it, then where it is now. Each game is found at the same place inside the new folder, or else by
  its file name anywhere under it, and keeps its favorites, artwork, sort title and emulator choice.
  If you already used Add Folder on the new place, those extra entries are merged back into the
  originals. S-Dock shows what will change before doing it.
- Nothing is written outside the folder. If the folder can't be written to (for example under
  `C:\Program Files`), S-Dock asks you to move it instead of saving somewhere else.
- S-Dock was called Switch Yard before; the old `SWITCHYARD_…` environment variables still work.

## For developers

```powershell
dotnet build -c Release                # build
dotnet test                            # run the automated tests
powershell -ExecutionPolicy Bypass -File build\package.ps1   # standalone app + installer
```

`build\package.ps1` publishes the app (self-contained: no .NET needed on the PC) and uses
[Velopack](https://velopack.io/) (`vpk`, a local tool in `dotnet-tools.json`) to lay it out as a
portable folder, then zips that as `releases\S-Dock-<version>-win-x64.zip` with an `emulators`
folder, plus the update packages. Velopack's installer isn't shipped, because it would install
into `%LOCALAPPDATA%`. The version comes from `Directory.Build.props`.

**Making a release:**

1. `powershell -ExecutionPolicy Bypass -File build\new-release.ps1 -Version 1.0.1` sets the
   version and creates `docs\releases\v1.0.1.md` from `docs\releases\TEMPLATE.md`.
2. Fill in **What's new** in that file, then commit and push.
3. `build\package.ps1` builds the zip, writes its checksum and the commit into the notes, and saves
   `releases\RELEASE-NOTES.md`, ready to paste into the GitHub release.
4. Publish the GitHub release as a full release (not a pre-release) with **every** file in `releases\`
   attached: the zip for downloads, the rest for automatic updates. Copies of S-Dock offer the
   update the next time they start.

Both output folders are git-ignored. Pass `--data-dir <folder>` to S-Dock to use a separate,
throwaway library.

| Path | What it is |
|---|---|
| `src/SDock.Core` | Library database, folder scanner, emulator launcher, metadata sources. No UI code. |
| `src/SDock.App` | Avalonia desktop app: views, view models, controller input |
| `tests/SDock.Tests` | xUnit tests, including headless UI tests and fake online services |
| `tests/FakeEmulator` | Stand-in emulator used by the launch tests |
| `emulators/`, `data/` | Your local emulators and library when run from source. Git-ignored. |

The tests never touch real games, a real emulator or real online services. See
[docs/TESTING.md](docs/TESTING.md) for what they cover, the manual checklist with a real
emulator, and performance measurements.
