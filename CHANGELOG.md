# Changelog

All notable changes to Sherpa Manager are recorded here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and releases follow
[Semantic Versioning](https://semver.org/spec/v2.0.0.html). While the project is
pre-1.0, minor versions may still contain breaking changes to profile data.

Releases are published from tags of the form `v<version>`. The tag, the
`<Version>` in [Directory.Build.props](Directory.Build.props), and the heading in
this file must all agree; the release workflow fails the build when they do not.

## 0.4.7

### Added

- Global shortcuts. Click **Set shortcut** on a profile and press the keys you
  want, such as Ctrl+Alt+W; the combination is recorded as you press it rather
  than typed out. It then activates that profile from anywhere in Windows. A
  combination another application already owns, or one claimed by two profiles,
  is reported and skipped rather than failing silently.
- `SherpaManager.exe --activate <profile>` switches by name, matched without
  case sensitivity. If Sherpa Manager is already running, the request is handed
  to the running copy instead of starting a second one that would fight it for
  the display. `--help` lists the arguments.
- **Create desktop shortcut**, which writes a desktop shortcut that activates
  the selected profile.
- **Settings -> Start Sherpa Manager when Windows starts**, registered per user
  so it needs no administrator rights.
- **Settings -> Activate at startup**, which applies a chosen profile when
  Sherpa Manager launches.

### Changed

- Duplicating a profile no longer copies its global shortcut, since two profiles
  cannot own one combination.
- Renaming, duplicating, and deleting a profile are reachable without scrolling.
  Right-clicking a profile in the list offers Rename, Duplicate, and Delete;
  previously those buttons sat below the application list where they were easy
  to miss. The profile name field is underlined so it reads as editable.
- Context menus follow the Sherpa dark theme instead of the Windows light
  chrome, which rendered light text on a white background.
- Right-clicking a profile now selects it, so the menu always acts on the
  profile that was clicked rather than the one previously selected.
- Refusing to delete the last remaining profile now says so instead of doing
  nothing when the button is clicked.

### Fixed

- **Browse** no longer leaves an empty application row behind when the file
  dialog is cancelled. The row is created only once a file has been chosen.
- **NVIDIA settings** opens the NVIDIA app directly on **System -> Display**,
  where Surround is configured, using the `nvidiaapp://route/#nvapp/...` scheme
  NVIDIA registers. Older systems still get NVIDIA Control Panel. Messages that
  told you to open NVIDIA Control Panel now name the NVIDIA app, since recent
  drivers no longer install a Control Panel.
- **NVIDIA settings** detects which NVIDIA software is actually installed: the
  NVIDIA app, the driver-installed Control Panel in either of its two locations,
  or the Store-packaged Control Panel. Every candidate is verified before it is
  launched, including that the registered protocol handler still points at a file
  that exists.
- **NVIDIA settings** opens the NVIDIA app instead of the Documents folder. It
  targeted the old NVIDIA Control Panel Store package by identifier; on machines
  where NVIDIA has replaced the Control Panel with the NVIDIA app that package no
  longer exists, and asking Explorer to open an unknown identifier silently opens
  Documents rather than failing. Sherpa now locates the installed application and
  says so plainly when there is none.
- An application started minimized can be opened again. The window watcher polls
  for up to 45 seconds so a launcher that opens its real window late is still
  minimized, but it used to re-minimize every matching window on every poll,
  including one the user had just restored. It now acts on each window once.

### Added

- **Settings -> Wait after display changes**, applied between the display
  layout and any application being started or closed. Windows returns from the
  topology call before the monitors have finished re-syncing, and an application
  started into that window can open on the wrong monitor or at the wrong size.
  Defaults to 3000 ms; set it to 0 for the previous behaviour.
- `SHERPA_TEST_FILTER` runs a subset of the test executable by name substring.

## 0.4.6

### Added

- An activation preview. Before a profile switch runs, Sherpa shows what it
  would do: monitors that will be enabled or disabled, resolution, refresh
  rate, rotation and primary-display changes, the NVIDIA Surround transition,
  and which applications will start, stay running, or be closed. Missing
  executables and missing working directories are reported as problems before
  anything is applied rather than as warnings afterwards.
- **Settings -> Show what a profile switch will change before it runs**, on by
  default, along with a **Do not show this again** option in the preview
  itself.

### Changed

- The bug report template now asks for the **Settings -> Copy diagnostics**
  report, how Sherpa Manager was installed, and how the reporter recovered from
  any bad display state. It also restates what the diagnostics report can
  contain so reporters can review it before pasting.
- Release notes point at the installer for checksum verification, matching the
  download the README recommends.

## 0.4.5

### Added

- x64 release packaging: a tag-triggered workflow that publishes
  framework-dependent and self-contained `win-x64` ZIP archives with a
  `SHA256SUMS.txt` checksum file.
- A Windows installer (`...-win-x64-setup.exe`). It installs per user by
  default so no administrator prompt appears, offers an all-users install,
  creates Start Menu and optional desktop shortcuts, and registers an
  uninstaller. Captured profiles and display snapshots under
  `%APPDATA%\SherpaManager` are left in place when uninstalling.
- A portable single-file build (`...-win-x64-portable.exe`). One executable,
  no unpacking and no separately installed .NET runtime.
- This changelog.

### Changed

- The supported platform is now declared explicitly. All projects build as x64
  via `Directory.Build.props`, matching the `nvapi64.dll` binding that NVIDIA
  Surround support depends on.
- The product version is defined once in `Directory.Build.props` instead of per
  project.

### Fixed

- The version reported by the application, the diagnostics report, and the bug
  report template said `0.2.0` while the project had reached 0.4.4. All three
  now report the real version.

## 0.4.4

### Added

- Diagnostics: rotating local JSONL logs, capture of Windows/AppDomain/WPF
  dispatcher and background-task exceptions, activation timing, Windows and
  NVAPI result codes, topology records, and process-matching decisions.
- **Settings → Copy diagnostics** for producing a shareable report.
- Paths and command-line arguments are redacted from diagnostics by default.

## 0.4.3.1

### Changed

- Documentation updates.

## 0.4.3

### Fixed

- The saved display verification is invalidated when the environment
  fingerprint changes. Monitor, GPU, port, driver, Windows, resolution,
  refresh-rate, and Surround changes require the confirmation countdown again
  rather than reusing a stale confirmation.

## 0.4.2

### Added

- Crash recovery: interrupted display transactions are detected on the next
  launch and offered for restore, covering the case where the live countdown
  could not run because the process or system stopped mid-transaction.

## 0.4.1

### Added

- A Cancel button on the busy overlay, with Escape support, that restores the
  previous display layout and application state.

## Earlier releases

Versions before 0.4.1 predate this changelog and are not itemized here. See the
commit history for details.
