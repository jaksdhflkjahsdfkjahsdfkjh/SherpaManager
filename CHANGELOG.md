# Changelog

All notable changes to Sherpa Manager are recorded here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and releases follow
[Semantic Versioning](https://semver.org/spec/v2.0.0.html). While the project is
pre-1.0, minor versions may still contain breaking changes to profile data.

Releases are published from tags of the form `v<version>`. The tag, the
`<Version>` in [Directory.Build.props](Directory.Build.props), and the heading in
this file must all agree; the release workflow fails the build when they do not.

## Unreleased

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
