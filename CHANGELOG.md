# Changelog

All notable changes to Sherpa Manager are recorded here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and releases follow
[Semantic Versioning](https://semver.org/spec/v2.0.0.html). While the project is
pre-1.0, minor versions may still contain breaking changes to profile data.

Releases are published from tags of the form `v<version>`. The tag, the
`<Version>` in [Directory.Build.props](Directory.Build.props), and the heading in
this file must all agree; the release workflow fails the build when they do not.

## 0.5.0

Quality-of-life release.

### Added

- Applications that will not work are flagged in the profile editor: the row
  number is replaced by a warning marker, the reason is in its tooltip, and a
  count appears beside the Applications heading. A flagged entry takes no
  number, so the numbers that remain read as the sequence that will run. Covers an executable or shortcut that cannot be
  resolved, a working directory that does not exist, and an entry that starts the
  same thing as an earlier one and would be skipped. The same rules the
  activation preview applies, so the two cannot disagree.
- **Recent switches**, a button beside the status bar, shows what recent profile
  switches actually
  did: every step in the order it happened, the seconds it took, warnings
  highlighted, and the outcome. **Copy** puts one switch on the clipboard as
  text for a bug report. The history is built from the same messages shown
  during the switch, so it cannot drift from what was seen, and cancelled or
  failed switches are recorded too. Kept in memory for the session; the rotating
  diagnostic log remains the record that survives a restart. Each switch is
  numbered for the session and drawn as a card with a coloured edge, so a long
  run of switches stays readable: purple when it succeeded, amber when it
  finished with warnings, red when it failed. The newest carries a badge, and
  each shows both the clock time and how long ago it was.

- **+ Add app** now opens a searchable list of the applications installed on
  this PC, read from the Start menu the way Windows reads it, with icons and the
  publisher each one came from. Type to filter by name, publisher, or path;
  choose several at once; the name, target, arguments, and working directory are
  filled in from the shortcut. Uninstallers, help links, and shortcuts whose
  target has gone are left out, and two shortcuts to the same executable become
  one entry unless they pass different arguments. Not everything installed has a
  Start menu entry, and a game launched through a protocol address is not a file
  at all, so the same window still offers choosing a file and adding an empty row
  by hand.
- Applications can be reordered by dragging a row, not only with the arrow
  buttons. Dragging is locked by default so a stray drag while editing cannot
  silently change the launch order; a padlock button beside the arrows unlocks
  it, and the choice is remembered. The padlock is drawn from the setting it
  controls, and its two states differ in colour as well as glyph, because an
  open and a closed padlock look nearly the same at that size.

### Fixed

- Waiting for an application to start now waits for something. The default rule
  was satisfied as soon as a matching process existed, which is true the instant
  an application is launched, so every application in a profile started at once
  and the order was decorative. Measured against a fixture that sleeps three
  seconds before opening, the process was there after 262ms and actually ready
  after 3,324ms. The rule now waits for the process to finish starting and reach
  its message loop, which also holds for tray applications that never show a
  window; anything without a message loop, such as a console application, is
  treated as ready immediately, as before. The setting is relabelled
  "It to finish starting".
- The warning flags and the launch order now follow the application list as it
  is edited. They were refreshed by each editor action asking for it, and two
  actions never did: browsing for an executable left the new entry unnumbered
  and unchecked for problems, and the up and down buttons left the numbering
  stale. The list is watched instead, so an editor action added later cannot
  forget.

### Changed

- The application columns no longer sort when their headers are clicked.
  Sorting the view showed an order that was not the order applications start
  in, which made the launch order unreadable.
- The status bar keeps showing the result of a switch. Window minimizing and
  application closing are watched for up to 45 seconds afterwards, and a
  successful outcome from one of those watchers used to replace the result with a
  note about a single application. Only failures from them take the status bar
  now.
- A profile's closing message names the switch — "Switched to iRacing" — rather
  than reading "iRacing is ready", which was indistinguishable from the readiness
  message of an application that shares the profile's name.
- The status bar reports the result of a switch rather than its last progress
  message, and colours it: plain when a switch succeeded, amber when it finished
  with warnings or was cancelled, red when it failed, with the error text
  included. Individual warnings are reported as they happen and kept in the
  switch history, so the closing message counts them instead of concatenating
  them into a line too long to read.
- Per-profile audio output and input. A profile can make a chosen playback and
  recording device the Windows default when it activates, covering both ordinary playback and
  communications. The switch happens before applications start, because most sim
  and voice applications read the default output once at launch and never look
  again. When the profile also changes displays, Sherpa waits up to 15 seconds
  for the chosen device to appear: a monitor's audio endpoint does not exist
  until Windows has finished enabling that monitor, and it arrives some time
  after the display itself is usable. A device that is not connected, or a switch
  Windows refuses, is reported as a warning and never fails the profile switch.
  Output and input are independent: a profile may set either, both, or neither.
- Launch order. Applications start in the order listed, now numbered in the
  grid, and by default each one waits for the previous to start before it
  begins. "Start this after that" is expressed by putting it lower in the list;
  there is nothing else to configure. **Settings -> Before starting the next app,
  wait for** raises that to the previous application's window appearing or
  responding, or turns waiting off, with a timeout. Applications Sherpa cannot
  track, such as scripts and protocol URLs, are never waited for, since no rule
  could ever be satisfied for them. A wait that times out is a warning: later
  applications still start.

### Changed

- The per-application launch delay is hidden by default and enabled from
  **Settings -> Show the per-application launch delay column**. Order and
  readiness cover almost everything; a fixed delay is only needed for scripts,
  protocol URLs, and other targets Sherpa cannot detect. Existing delays keep
  working whether or not the column is shown.
- The application version is shown in the bottom right corner of the window, so
  a bug report can name the exact build without opening Settings.
- **View layout** in the profile editor draws the monitors a profile will
  arrange, to scale and in their real positions, with the primary display
  highlighted and each panel labelled with its resolution, refresh rate, and
  rotation. Captured NVIDIA Surround grids are shown alongside: topology,
  per-display mode, bezel correction, colour depth, and panel order. The dialog
  sizes itself to its content, up to the height of the screen, so the layout is
  not hidden behind a scrollbar.

### Fixed

- Per-profile audio output had no effect. The audio service was created after the
  services that consume it, so both the activation and the preview received null
  and skipped the step silently. Activation now records whether an audio device
  was requested and whether the service was available, so a skipped step is
  visible in diagnostics instead of looking like a switch that did not work.
- Tooltips follow the Sherpa theme instead of the Windows default light one.
  They are drawn from WPF's own style rather than the window's, so a dark
  application shows light tooltips until it says otherwise. Long text now wraps
  within a bounded width rather than stretching across the screen.
- Dialogs use the dark title bar instead of the Windows default white one. Only
  the main window opted in, so every dialog Sherpa opened had a white caption
  above dark content.

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
