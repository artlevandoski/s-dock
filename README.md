# S-Dock

A controller-friendly Windows frontend for certain game libraries. Point it at your game
folders and your own emulator, and browse everything in a grid or a full-screen carousel
with covers, backgrounds, logos and descriptions.

You bring your own game files and emulator. S-Dock does not include or download games,
firmware, keys or system files, and it never changes your game files.

Built with C# / .NET 10 and [Avalonia](https://avaloniaui.net/). **Status: beta (0.1.0-beta.1).**

## Contents

- [Install](#install)
- [Getting started](#getting-started)
- [Using S-Dock](#using-s-dock)
- [Game info and artwork](#game-info-and-artwork)
- [Controller and keyboard](#controller-and-keyboard)
- [Portable folder](#portable-folder)
- [For developers](#for-developers)

## Install

S-Dock runs on 64-bit Windows 10 or 11, and there's nothing to install: it's one folder.

1. Download `S-Dock-<version>-win-x64.zip` and unzip it wherever you like: Documents, another
   drive, a USB stick. You get an **S-Dock** folder.
2. Run `S-Dock.exe` inside it. Put your emulators in its `emulators` folder.

Everything S-Dock saves (your library, artwork, TitleDB and settings) goes into the `data` folder
beside it, and nowhere else on your PC. To move S-Dock, move the folder; to remove it, delete the
folder. Don't put it under `C:\Program Files`: Windows doesn't let programs save there, and S-Dock
will ask you to move it.

The beta isn't code-signed yet, so Windows SmartScreen may say it "protected your PC": choose
**More info → Run anyway**.

**Bringing an existing library across:** close S-Dock, then copy the `data` and `emulators`
folders from your old S-Dock folder into the new one.

## Getting started

To run from source instead, you need the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0):

```powershell
dotnet run --project src\SDock.App
```

Then, from the **Settings** menu (the gear, or Select on a controller):

1. **Emulator Settings…** (Ctrl+E): add your emulator. Builds placed in the `emulators\`
   folder are found automatically; **Add as profile** sets them up. For Eden, **Use Eden
   preset** fills in the right arguments, and **Test Launch** checks it.
2. **Add Folder…** (Ctrl+O): pick a folder of game files (`.nsp`, `.xci`, `.nsz`, `.xcz`). A
   preview lets you set each game's generation and fix titles before importing. Updates,
   DLC and duplicates are left out by default.
3. When asked, let S-Dock download **TitleDB** to fill in names, descriptions and pictures.

**PC versions** (recompilations and ports that are their own `.exe`): use **Settings → Add Game
(.exe)…**. They run on their own, without an emulator, and are filed as Switch 2 (change it in
the details panel). A title ID in brackets in the file or folder name, like `[0100…]`, lets
TitleDB fill them in; otherwise use **Game Info and Matching → Set Title ID…**.

## Using S-Dock

**Libraries.** *Main Library* and *Arcade Archives* are separate tabs, each with its own
search, filters and last selection. Arcade Archives and Arcade Archives 2 titles go there
automatically; turn on **Settings → Arcade Archives → Include ACA NEOGEO / Include SEGA AGES** to add those
series too.
The **Library** dropdown in the details panel moves any game by hand.

**Grid or carousel.** The grid shows a details panel beside the covers. The carousel fills
the screen with one row of covers above the game's name and the first sentence of its
description (**Edit Carousel Text…** changes it); switching back to the grid leaves full
screen. Both views share the same selection. The carousel is kept clean: search, Add Folder,
the filter bar and the view buttons appear only in the grid (Y or Ctrl+G switches views, Ctrl+F
jumps to the grid's search, and filters are under **Settings → Filter** in either view).

**Jump by letter.** Up/Down in the carousel, LT/RT on a controller, or Page Up/Page Down jump
to the next or previous letter (by sort title). The letter shows briefly in the middle.

**Filters.** All Games, Switch 1 and Switch 2, plus **More filters**: Unclassified, Missing
files, Update available, Not in TitleDB, Needs IGDB match and Hidden games. With a
controller, use **Settings → Filter**.

**Playing.** **Play** (A, Enter or double-click) uses the game's own emulator choice, then
its generation's default, and otherwise asks you. **Launch With…** picks an emulator just
once. When the game closes, S-Dock comes back with the same game selected.

- **No emulator** is a choice in each game's **Emulator** dropdown, and for the Switch 2
  default in **Emulator Settings**. It runs the game's own `.exe`. PC versions always run this
  way.

- **Esc** (while the game is in front) or **Start + Select** closes the game and returns to
  S-Dock. By default you press twice: the first press shows a note, and a second press within
  3 seconds closes it. Change this under **Settings → Playing**.
- **Start Games in Full Screen** (on by default) starts Eden in full screen. For other
  emulators, add their own full-screen switch to the profile.

**Menus.** Game Actions (the X button, the Game Actions button, or right-click a cover) and
Settings (Select, or the gear) open as a list in the middle of the screen. Move with the D-pad
or arrow keys, choose with A or Enter, and go back with B or Esc. Settings with a tick apply
straight away.

**Game Actions:**

| Action | What it does |
|---|---|
| Launch With… / Play Version… | Play once with another emulator, or another file of the game |
| Choose Cover / Background / Logo… | Pick from downloaded artwork or browse your PC; *Use Default* goes back |
| Edit Description… / Edit Carousel Text… | Write your own description, or the one line shown in the carousel |
| Add Version File… | Attach another file (an update, revision or alternate dump) to the game |
| Sort Title… | Sort the game under another name, e.g. "Zelda 1", without renaming it |
| Hide Game | Hide it from its library; find it again under **More filters → Hidden games** |
| Game Info and Matching › | Refresh from TitleDB, **Set Title ID…**, match or refresh on IGDB, use the default artwork, or **Clear Match and Artwork…** to start the game over |

For now, Game Actions are available in the grid only; the carousel is kept to browsing and
playing.

**Updates and DLC.** S-Dock reads the update and DLC files beside each game (for example in
`Updates` and `DLC` subfolders) from their file names. The details show whether you have
the latest known update and how many DLC you have.

**Settings** also has **Background** (game artwork, a gradient in the game's colour, or
plain), **Playing**, **Game Info** (TitleDB, nx-versions, IGDB, logos), **Open Data Folder**,
**Open Emulators Folder** and **Quit**.

## Game info and artwork

S-Dock combines several sources. Your own choices always come first, then TitleDB, then
IGDB.

| Source | Gives | Setup |
|---|---|---|
| [TitleDB](https://github.com/blawar/titledb) | Name, description, publisher, release date, genres, players, square eShop icon (the cover), banner (the background), screenshots | None. Matched exactly by each file's title ID. |
| [nx-versions](https://github.com/16BitWonder/nx-versions) | Latest known update for each game, and dates | None. On by default (**Settings → Use nx-versions**). |
| [IGDB](https://www.igdb.com/) | Backup for games TitleDB doesn't list, and logos | Free Twitch developer credentials (below) |
| [SteamGridDB](https://www.steamgriddb.com/) | More logos, covers and backgrounds | Free API key (optional) |

**TitleDB.** **Settings → Update from TitleDB** downloads the US English listing, then the UK,
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
3. In S-Dock, **Settings → Metadata Settings…**: paste both, **Test IGDB**, then **Save**.

Then use **Match Unmatched Games on IGDB** for games without info, or **Match on IGDB…**
(details panel → Metadata) for one game. You can search, paste an igdb.com link, or enter
an IGDB ID.

**SteamGridDB setup (optional):** sign in, open *Preferences → API*, generate a key, and
paste it into **Metadata Settings…**. Then run **Get Missing Logos**.

Keys are stored only in `data\library.sqlite3` on your computer.

## Controller and keyboard

Xbox-compatible controllers work through Windows XInput, with no extra drivers.

| Controller | Keyboard | Action |
|---|---|---|
| D-pad / left stick | Arrow keys | Move between covers (hold to repeat); in the carousel, Up/Down jump letters |
| A | Enter | Play, or choose in a menu |
| B | Esc | Back / close a menu or dialog |
| X | Right-click | Game Actions |
| Select (Back) | | Settings |
| Y | Ctrl+G | Grid / carousel |
| LB / RB | Ctrl+Shift+Tab / Ctrl+Tab | Previous / next library |
| LT / RT | Page Up / Page Down | Previous / next letter |
| Start | | Play |
| Start + Select | Esc (in a game) | Close the game and return to S-Dock |
| | Ctrl+F | Search |
| | Ctrl+O / Ctrl+E | Add Folder / Emulator Settings |

In dialogs (questions, choosers, pickers, text editors, settings windows) the D-pad moves through
lists and then between buttons, A presses or chooses (it ticks check boxes and opens dropdowns),
and B cancels. Typing and Windows' own file and folder pickers still need a keyboard and mouse.

Controller input is ignored while another app is in front, except Start + Select during a
game. In full screen, S-Dock brings itself to the front when you return from a game.

## Portable folder

S-Dock keeps everything beside itself, so the whole folder can move to another PC or a USB
drive:

```
S-Dock\
  S-Dock.exe
  data\        library database, artwork, TitleDB and logs
  emulators\   emulator builds you place here
```

- Emulators inside the folder are remembered relative to it, so they keep working after a
  move. Game folders elsewhere are remembered by their full path.
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
into `%LOCALAPPDATA%`. The version comes from `Directory.Build.props`, or pass `-Version 0.1.0-beta.2`.
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
