# Metis Windows installer

This folder contains the reproducible installer definition for Metis.

## Build

Install the 64-bit edition of Inno Setup 7, then run:

```powershell
.\installer\build-installer.ps1
```

To assign a release version:

```powershell
.\installer\build-installer.ps1 -Version 1.0.0
```

The script restores, builds, tests, and publishes Metis as a compressed,
self-contained `win-x64` application before compiling:

```text
installer\output\Metis-Setup-1.0.0-win-x64.exe
```

Use `-SkipTests` only when tests have already passed for the same commit. A
custom compiler can be selected with `-InnoCompilerPath`.

## What the setup installs

- Metis's self-contained Windows application (no separate .NET runtime needed)
- Start Menu shortcut
- Optional desktop shortcut
- Per-user uninstall entry

Provider keys remain in Windows Credential Manager. User settings remain under
`%LOCALAPPDATA%\Metis` across upgrades and uninstalls.

Large third-party AI weights and machine-specific Python environments are not
embedded. Install Ollama and the selected local model separately when using the
fully local reasoning profile. New installations can use Metis's native Windows
speech providers immediately.
