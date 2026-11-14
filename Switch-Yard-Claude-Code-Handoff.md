# Switch Yard build brief

## Start here

Build Milestone 1 in the current Windows project directory. Inspect any existing files and project instructions first. Use Python, PySide6, and SQLite. Create a virtual environment, document the Windows setup and launch commands, and run the available checks. Use a fake launcher and placeholder files for automated checks. Leave the real emulator launch for an interactive Windows smoke test with the user's existing Eden installation and game folder.

Switch Yard is an independent Windows frontend for Switch 1 and Switch 2 game libraries. Keep the Switch Yard name and generation labels. Users bring their own games and configure their emulator installations. Package the frontend with its own branding and placeholders. Game files, firmware, keys, and system files are outside the distribution and download features.

## Milestone 1 · Launch a game from the library

- Build a compact desktop window with Add Folder, a cover grid with placeholders, a selected-game details panel, Emulator Settings, and Play.
- Add a folder scan with a preview before import. Let the user assign a generation to the folder or correct entries in the preview. Flag suspected patches, DLC, and duplicates for review. Treat ambiguous filenames as unclassified and leave original files untouched.
- Store editions under stable internal IDs. Store local launch paths separately and allow multiple files per edition later. Reserve nullable console title ID and IGDB ID fields. Keep generation and library assignment separate from emulator profiles.
- Persist the library and profiles in SQLite in a user-writable local app data folder. Preserve existing records during repeat scans and show missing paths clearly.
- Start with one existing Eden executable selected via a file picker. Store its executable, working directory, supported generation, and argument list in an editable profile. Use the documented Eden game argument as a preset and let the user verify it with Test Launch.
- Pass the executable and arguments separately through Qt process APIs. Verify the paths, handle startup failure, and restore the selected cover and focus when the emulator exits. Keep launch logic callable from both Play and Test Launch.
- Add checks for repeat scans, persistence after restart, paths containing spaces, missing executable and game paths, and launch completion using a test process. Document the real Windows smoke test and record what passed.

Milestone 1 is ready for review when you can import a small folder, reopen the app, launch a game, close the emulator, and return to the same cover. Include a short summary of changed files, Windows run commands, and test results.

## Subsequent milestones

1. **Emulator profiles and launch routing.** Add named profiles for multiple installs and custom executables. Play uses the per-game override, then the generation default, then the profile picker. Launch With… is a one-time choice. Support two versions of the same emulator. Verify presets against actual Windows builds before labeling them supported.
2. **Grid, Carousel, and libraries.** Add a controller-friendly horizontal row with the centered cover roughly 15–20% larger than its neighbors. Share selection across views and remember view and last selection per library. Main Library and Arcade Archives are separate browsing scopes with their own search and filters. Classify Arcade Archives and Arcade Archives 2 by title prefix, with persistent manual overrides. Offer ACA NEOGEO as an optional inclusion. Use Play Version… for alternate files. Keep distinct Switch 2 editions separate by default.
3. **IGDB metadata and artwork.** Fetch descriptions, genres, game logos, covers, and landscape key art. Use platform-aware matching, persist the chosen IGDB ID, and cache assets locally. Add a Needs Match filter and Match on IGDB… with editable search, result previews, direct URL or ID, and Change Match. Preserve user-edited text and art on refresh. Store user credentials outside source control and keep any shared secret out of a distributed client. Videos follow later.
4. **Game Updates & DLC.** Make this the first AeroFoil integration. Validate the authenticated catalog response against a few games with known local content before building the UI. Match by console title ID and compare latest known update and add-ons with the connected library's inventory. Show missing items, items present in the library, source, and last check. Show unknown when offline, unmatched, or uncovered. Verify coverage per generation. Treat emulator installation status as a separate future capability. The connector reads status only.
5. **Emulator Manager.** Find installed executables, link portable builds, and offer official project download links. Add managed Eden Stable downloads first, with release notes, optional update notifications, manual approval, verification where published, a staged install, and rollback. Preserve saves and configuration using the emulator's supported layout. Check that the emulator has exited before changing the active version. Add experimental builds as opt-in profiles.
6. **Library maintenance.** Add Needs Attention, folder relinking, compatibility notes per game and emulator build, and saved collections. Consider a read-only LaunchBox import after the native importer is reliable.

## Windows smoke test checklist

- Import a small folder that includes a path with spaces and one Arcade Archives title. Verify preview classifications and repeat import behavior.
- Restart the app and verify that the game records and profile persist.
- Launch a known working title in Eden, then exit Eden and verify focus returns to the selected cover.
- Try a moved game path and a missing executable. Verify that the app identifies the item to fix and remains usable.
