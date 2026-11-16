# S-Dock {{TITLE}}

**S-Dock** is a controller-friendly Windows frontend for switch game libraries. Point it at your own
game folders and your own emulator, and browse everything in a full-screen carousel or a grid, with
covers, backgrounds, logos and descriptions filled in for you.

> S-Dock does not include or download games, firmware, keys or system files, and it never changes
> your game files. You bring your own games and emulator.

## What's new

- (Write the changes in this release here, one per line.)

## Download

**`{{ZIP}}`**: unzip it and you have an **S-Dock** folder with everything in it. There's nothing to
install.

The other files (`.nupkg`, `releases.win.json`, `assets.win.json`, `RELEASES`) are for future
in-place updates; you don't need to download them.

**Requirements:** 64-bit Windows 10 or 11. Nothing else needs installing.

**Updating from an earlier version:** unzip this one, then copy the `data` and `emulators` folders
from your old S-Dock folder into the new one.

## Getting started

1. Unzip the download wherever you like: Documents, another drive or a USB stick (not
   `C:\Program Files`, where Windows doesn't let programs save).
2. Run `S-Dock.exe` in the S-Dock folder. S-Dock isn't code-signed yet, so Windows SmartScreen may
   say "Windows protected your PC": choose **More info → Run anyway**.
3. Open **Settings** (the gear, or Select on a controller):
   - **Emulators → Emulator Profiles…** to add your emulator (put emulator builds in the S-Dock
     folder's `emulators` folder and they're found automatically);
   - **Library → Add Folder…** to add your games;
   - let S-Dock download **TitleDB** when it asks, to fill in names, descriptions and pictures.

Everything S-Dock saves (library, artwork, TitleDB, settings) stays in the `data` folder inside the
S-Dock folder, and nowhere else on your PC. To move S-Dock, move the folder; to remove it, delete
the folder.

## Known limitations

- Game Actions are available in the grid only; the carousel is kept to browsing and playing.
- Typing (search, text boxes) and Windows' file and folder pickers still need a keyboard and mouse.
- Automatic updates aren't switched on yet.
- The app isn't code-signed (see the SmartScreen note above).

## Feedback

Please report problems and ideas in [Issues](https://github.com/artlevandoski/s-dock/issues). It
helps to include the version (Settings → About) and, after an error, the
file `data\logs\errors.log`.

<!-- build-info: filled in by build\package.ps1 -->
<!-- /build-info -->
