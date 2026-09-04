# Sherpa Manager

Sherpa Manager is a Windows utility for switching a PC between work and sim-racing setups. A profile can restore the required Windows monitor topology and captured NVIDIA Surround grid, close applications from the previous profile, and start its own ordered list of companion applications.

> [!IMPORTANT]
> Sherpa Manager 0.2 is an early release. Display switching is hardware- and driver-sensitive. Read the recovery guidance before testing a new monitor or NVIDIA Surround configuration.

## Features

- Capture which monitors are enabled, including their positions, resolutions, orientations, refresh rates, and primary display.
- Switch between topologies such as one work monitor and three sim-rig monitors; omitted monitors are disabled from the Windows desktop.
- Capture, validate, and restore an NVIDIA Surround grid through the official [NVAPI Mosaic interface](https://docs.nvidia.com/nvapi/group__mosaicapi.html), including panel order, bezel overlap, bezel-corrected mode selection, rotation, resolution, color depth, and refresh rate.
- Validate monitor identities, verify the applied topology, and automatically roll back a failed display change.
- Keep an on-disk emergency display snapshot and offer **Restore last** from the window and tray.
- Apply a newly captured layout temporarily the first time, then save it to Windows only after a 10-second confirmation. Sherpa asks again if the display environment later changes.
- Cancel a profile or display operation while Sherpa safely restores the state from before it started.
- Copy privacy-redacted diagnostics backed by a local rotating structured log.
- Launch applications in order, resolve `.exe`, `.lnk`, `.url`, script, and protocol targets, and suppress duplicate entries.
- Minimize delayed launcher windows such as MOZA Pit House after their real UI process appears.
- Close visible or hidden application windows and automatically force-close unresponsive apps selected for closing.
- Activate the existing Sherpa window when opened again instead of creating another instance.
- Choose whether closing the window exits fully or keeps Sherpa in the tray. The normal minimize button remains on the taskbar.

The first launch creates empty **Work**, **iRacing**, and **ACC** examples without assuming machine-specific paths.

## Requirements

- Windows 10 or Windows 11, x64
- [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) for framework-dependent builds
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) only when building from source
- An NVIDIA driver exposing NVAPI when using automatic Surround switching

Sherpa Manager is x64 only. NVIDIA Surround support binds `nvapi64.dll` directly, which a 32-bit process cannot load, so every build is pinned to x64 in [Directory.Build.props](Directory.Build.props).

## Install

Download from the [Releases page](../../releases). Most people want the installer. Sherpa Manager keeps its profiles, display snapshots, and logs under `%APPDATA%\SherpaManager` and `%LOCALAPPDATA%\SherpaManager`, and writes nothing else outside your user profile.

| Download | Size | Choose it when |
| --- | --- | --- |
| `SherpaManager-v<version>-win-x64-setup.exe` | ~60 MB | **Recommended.** Installs per user with no administrator prompt, adds Start Menu and optional desktop shortcuts, and registers an uninstaller. Includes .NET, so nothing else is needed. |
| `SherpaManager-v<version>-win-x64-portable.exe` | ~70 MB | You want one executable you can run from anywhere, including a USB stick, with nothing installed. |
| `SherpaManager-v<version>-win-x64.zip` | ~1.5 MB | You already have the .NET 8 Desktop Runtime and want the smallest download. Unpack and run `SherpaManager.exe`. |
| `SherpaManager-v<version>-win-x64-self-contained.zip` | ~68 MB | You want a portable folder rather than a single file, without installing .NET. |

Uninstalling through the installer leaves your captured profiles in place, so reinstalling does not lose your layouts. Delete `%APPDATA%\SherpaManager` by hand to remove them.

Every release also publishes `SHA256SUMS.txt`. Verify a download before running it:

```powershell
Get-FileHash .\SherpaManager-v<version>-win-x64-setup.exe -Algorithm SHA256
```

Compare the printed hash with the matching line in `SHA256SUMS.txt`. Releases are unsigned, so Windows SmartScreen may warn on first run; see [Known limitations](#known-limitations).

Changes for each version are listed in [CHANGELOG.md](CHANGELOG.md).

## Build, test, and run

```powershell
dotnet restore SherpaManager.sln
dotnet build SherpaManager.sln -c Release
dotnet run --project tests/SherpaManager.Tests/SherpaManager.Tests.csproj -c Release --no-build
dotnet run --project src/SherpaManager.csproj
```

Create a smaller framework-dependent build:

```powershell
dotnet publish src/SherpaManager.csproj -c Release -r win-x64 --self-contained false -o build/SherpaManager
```

Or create a larger self-contained build that does not require a separately installed .NET runtime:

```powershell
dotnet publish src/SherpaManager.csproj -c Release -r win-x64 --self-contained true -o build/SherpaManager-self-contained
```

The `build/` directory is intentionally ignored. Packaged binaries are published through GitHub Releases by [.github/workflows/release.yml](.github/workflows/release.yml) rather than committed; see [CONTRIBUTING.md](CONTRIBUTING.md) for the release procedure.

## Configure work and sim monitors

### Standard extended displays

1. Open **Windows display settings** from Sherpa.
2. For **Work**, choose **Show only on 1** (or whichever numbered display is the work monitor), then select **Capture current** in Sherpa.
3. For a non-Surround sim profile, choose **Show only on 2** or extend/arrange the rig displays as desired, then capture again.
4. Use **Test**. The layout remains temporary until you choose **Keep layout**; otherwise Sherpa restores the previous topology after 10 seconds.

**Capture current** stores the complete active Windows CCD topology. That includes the same active-monitor selection represented by Windows' **Show only on 1/2** choices, plus primary display, positions, resolution, orientation, and refresh rate. Applying the profile disables paths that were inactive when it was captured. The monitors remain physically connected; “disabled” means removed from the active Windows desktop, not physically powered off.

### NVIDIA Surround triples

1. Open **NVIDIA Control Panel** from Sherpa and configure/apply the 3×1 Surround topology, including bezel correction and refresh rate.
2. Capture the sim profile while Surround is enabled. Sherpa saves the complete active NVAPI display grid—including display IDs in row/column order, per-panel bezel overlap, bezel-corrected resolution choice, rotation, per-panel resolution, color depth, and refresh rate—and automatically selects **Require enabled**.
3. Disable Surround in NVIDIA Control Panel, choose the desired Windows **Show only on** work display, and capture **Work** again. Sherpa automatically selects **Require disabled** when the NVIDIA driver retains a configured topology.
4. Test both directions and keep each confirmed layout.

During activation Sherpa first validates and restores the saved NVIDIA grid (or disables Surround), applies and verifies the exact Windows monitor paths, and only then closes the old profile's applications and launches the target applications. If an old-profile application cannot be closed, Sherpa restores the previous display layout and restarts applications already closed by that switch. The NVIDIA change uses the current GPU topology and blocks any operation requiring a disruptive driver reload. If the saved displays are unavailable or NVAPI rejects the grid, Sherpa stops and restores the prior NVIDIA and Windows states instead of continuing blindly.

After you keep a layout, Sherpa stores its additional environment check as a one-way fingerprint rather than duplicating driver and operating-system details in the profile. Profile activation checks it after applying the temporary layout. The confirmation stays first-use-only while the setup is stable, but returns after material changes such as a monitor or port remap, GPU/display-driver update, Windows version change, resolution or refresh-rate change, or NVIDIA Surround grid/bezel change. Profiles created by older Sherpa versions are confirmed once to create this fingerprint.

While a profile switch, display test, or recovery operation is running, choose **Cancel** or press **Esc**. Sherpa stops the remaining work, closes target applications started by the cancelled switch, restores the previous display layout, and restarts previous-profile applications that it had closed.

## Configure applications

1. Add an executable, Windows shortcut, Internet shortcut, script, or protocol URL.
2. Leave **Minimized** enabled for companion utilities. Sherpa applies the normal minimized launch hint and then watches for delayed child windows.
3. **Close on switch** is enabled by default for newly added applications. Leave it enabled when the application belongs only to that profile: Sherpa first requests a normal exit and then force-terminates the matching process tree if it remains running. Disable it for software that may contain unsaved work; disabled applications are not closed or force-terminated during a profile switch.

Windows commonly hides `.url` and `.lnk` extensions. Sherpa resolves an unambiguous hidden extension automatically, and the file picker includes both formats. The standard Steam iRacing shortcut (`steam://rungameid/266410`) is automatically associated with `iRacingUI`; Sherpa deliberately does not manage the persistent iRacing service process. Sherpa follows child processes created by launchers. If a script or custom protocol does not reveal its launched process, select the actual executable when possible; otherwise Sherpa reports that it cannot manage that entry.

## Window and tray behavior

- Starting Sherpa again restores and activates the existing instance.
- Clicking the taskbar button or minimize button minimizes normally to the taskbar.
- **Keep Sherpa running in the tray when I close its window** controls the X button, Alt+F4, and other window-close commands. Turn it off to exit completely when closing.
- **Exit** in the tray always closes Sherpa completely.

## Display safety and recovery

Before every new display change, Sherpa saves `%APPDATA%\SherpaManager\last-display-recovery.json`. It also writes the immediate pre-change layout to `display-transaction-recovery.json` and creates a small `display-transaction.json` marker before touching the display state. The marker is removed only after the requested layout or its automatic rollback has been verified. If Sherpa, Windows, or the graphics driver stops during that interval, Sherpa offers to restore the pre-change layout on its next launch. Profile and normal display-recovery saves retain `.bak` copies, and Sherpa can load those copies if the primary JSON is damaged. **Restore last** does not overwrite the safe snapshot it is restoring.

Windows adapter and target IDs can change after a reboot or driver restart. Sherpa remaps them using the saved monitor device identity before applying a layout and fails closed when an identity is missing or ambiguous. Recapture both profiles after moving a monitor to another GPU or port, changing cabling, or replacing display hardware.

If a restored layout is unusable:

1. Wait for the 10-second confirmation dialog to revert automatically, or choose **Revert now**.
2. Choose **Restore last** from the Sherpa tray menu.
3. Press **Win+P** and choose **PC screen only** or **Extend**.
4. Open **Settings → System → Display** and restore a working arrangement.
5. Reconnect missing hardware and recapture profiles after changing monitors, cables, ports, GPUs, or display drivers.

## Data and privacy

Profiles are stored in `%APPDATA%\SherpaManager\profiles.json`. Structured diagnostic logs are stored locally in `%LOCALAPPDATA%\SherpaManager\Logs` as JSON Lines. Sherpa keeps at most five approximately 1 MB log files. Paths and command-line arguments are redacted before logging by default.

Use **Settings → Copy diagnostics** to copy the Sherpa and Windows versions, GPU/display-driver information, NVIDIA API state, current monitor topology, activation timing, native error codes, and recent process-matching decisions. The report can still include profile/application names, monitor model names, process names and IDs, so review it before sharing. Sherpa has no accounts, telemetry, or network service; nothing is uploaded automatically.

## Known limitations

- Windows only.
- Display layouts are hardware-specific and are not intended to move between computers.
- NVAPI Surround support depends on the installed NVIDIA GPU and driver. The sim profile must be captured while Surround is enabled because NVAPI only enumerates complete active grids; unsupported drivers and driver-reload-required topologies fail safely and require NVIDIA Control Panel.
- Scripts and custom protocols may be launch-only when Windows does not expose the process they create.
- Automatic force-closing can discard unsaved work; disable **Close on switch** for applications that must remain open safely.
- The live 10-second countdown requires Sherpa to remain running. If the process or system stops during a display transaction, recovery resumes as an explicit restore offer the next time Sherpa starts; Win+P and Windows Display Settings remain the final manual recovery paths.
- Downloaded unsigned builds may display a Windows SmartScreen warning.

## Contributing and security

See [CONTRIBUTING.md](CONTRIBUTING.md) before submitting a change. Report suspected vulnerabilities privately as described in [SECURITY.md](SECURITY.md).

Sherpa Manager is available under the [MIT License](LICENSE). Product names and trademarks belong to their respective owners. Sherpa Manager is independent and is not affiliated with or endorsed by the simulation titles represented by its example profiles.
