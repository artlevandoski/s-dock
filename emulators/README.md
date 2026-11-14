# Emulators folder

Put emulator builds here, one folder per build, for example:

```
emulators/
  Eden 0.2.1/
    eden.exe
  Eden nightly/
    eden.exe
```

Everything in this folder except this README is ignored by git, so nothing you place here
is uploaded. S-Dock opens this folder from **Settings → Open Emulators Folder** and
starts the executable picker here when you set up a profile.

If S-Dock's folder is read-only, `%LOCALAPPDATA%\S-Dock\Emulators` is used instead.
Emulators placed here are stored by relative path, so they move with the folder.
