# Sherpa Manager

Sherpa Manager is a Windows utility for switching a PC between work and sim-racing setups. Each profile can restore a captured Windows display topology and launch its own ordered list of companion applications.

> [!IMPORTANT]
> Sherpa Manager is currently an early release. Display switching has been tested on a limited number of configurations. Read the recovery guidance below before relying on it for a new monitor or GPU setup.

## Features

- Capture display positions, resolutions, orientations, and refresh rates per profile.
- Activate profiles from the main window or system tray.
- Launch applications in order with command-line arguments and optional delays.
- Skip applications that are already running.
- Ask selected applications to close when leaving a profile.
- Create, duplicate, rename, and delete profiles, and reorder their applications.
- Store profiles locally in an editable JSON document.

Sherpa never force-kills applications. The first launch creates empty **Work**, **iRacing**, and **ACC** examples without assuming machine-specific paths.

## Requirements

- Windows 10 or Windows 11, x64
- [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) for framework-dependent release builds
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) only when building from source

## Build and run from source

```powershell
dotnet restore SherpaManager.sln
dotnet build SherpaManager.sln -c Release
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

The `build/` directory is intentionally ignored. Publish packaged binaries through GitHub Releases instead of committing them to the repository.

## First setup

1. Arrange the displays for work in Windows **Settings → System → Display**.
2. Select **Work** in Sherpa and choose **Capture current**.
3. Arrange or enable the displays for the sim rig, select the relevant racing profile, and capture again.
4. Add each companion application with **Browse**.
5. Use `Delay ms` when one application needs time to initialize before the next one starts.
6. Leave **Close on switch** enabled only for applications Sherpa should ask to close when leaving that profile.

Closing the main window keeps Sherpa running in the system tray. Use **Exit** from the tray menu to stop it completely.

## Display safety and recovery

Display snapshots depend on the same monitors, adapters, and connections remaining available. Capture profiles again after changing monitors, cables, ports, GPUs, or relevant display drivers.

If a restored layout is unusable:

1. Press **Win+P** and choose **PC screen only** or **Extend**.
2. Open **Settings → System → Display** and restore a working arrangement.
3. Reconnect any missing monitor or cable if Windows no longer detects it.
4. Capture the corrected layout in Sherpa before activating that profile again.

## Data and privacy

Profiles are stored in `%APPDATA%\SherpaManager\profiles.json`. Sherpa has no accounts, telemetry, or network service. Executable paths and arguments remain on the local computer unless you share the profile file yourself.

## Known limitations

- Windows only.
- Display layouts are hardware-specific and are not intended to move between computers.
- Some applications do not respond to a normal close request and will remain open.
- Downloaded unsigned builds may display a Windows SmartScreen warning.

## Contributing and security

See [CONTRIBUTING.md](CONTRIBUTING.md) before submitting a change. Report suspected vulnerabilities privately as described in [SECURITY.md](SECURITY.md).

Sherpa Manager is available under the [MIT License](LICENSE).

Product names and trademarks belong to their respective owners. Sherpa Manager is an independent project and is not affiliated with or endorsed by the simulation titles represented by its example profiles.
