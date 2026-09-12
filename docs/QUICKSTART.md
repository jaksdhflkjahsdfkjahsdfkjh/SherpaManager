# Getting started with Sherpa Manager

Sherpa Manager switches display layouts, audio devices, and companion applications
between your work and racing profiles. It runs on Windows 10/11 x64.

## Install and open

Download the `win-x64-setup.exe` from
[GitHub Releases](https://github.com/jaksdhflkjahsdfkjahsdfkjh/SherpaManager/releases).
The installer includes .NET. The `portable.exe` is an alternative requiring no
installation; keep it in a permanent folder before enabling startup or creating
shortcuts. Both forms share the same user profiles. Exit the tray copy before
changing between them.

Releases are currently unsigned. Compare the download's SHA-256 with the release's
`SHA256SUMS.txt` using `Get-FileHash <download> -Algorithm SHA256`.

## Set up your first profiles

1. Start Sherpa. The initial Work, iRacing, and ACC profiles are empty examples.
2. Arrange your monitors in Windows Display Settings. For NVIDIA Surround,
   configure the grid in NVIDIA settings first.
3. Select the matching profile and choose **Capture current**. This saves your
   current layout; replacing a saved layout asks for confirmation.
4. Choose **Test** and **Keep layout** if the displays look right. Without
   confirmation, the temporary layout rolls back after ten seconds.
5. Select the audio devices you want and add your companion applications.
   Review **Close on switch** for each entry: an application that does not close
   normally can be force-closed. Disable it for applications with unsaved work.
6. Repeat for the other setup, then test switching in both directions. Enable
   global shortcuts, Windows startup, or startup activation after those tests.

On smaller windows, use the Display, Audio & shortcuts, and Applications sections
at the top of the profile editor. Previous/Next reveals setup or settings content
that cannot fit. Larger windows show the complete overview together.

## Recovery and support

To start Sherpa quietly at sign-in, enable **Settings → Start Sherpa Manager when
Windows starts**, then **Start minimized**. It starts in the tray if you keep
Sherpa running there when its window closes; otherwise it starts on the taskbar.
Opening Sherpa from the Start menu still brings up its window.

If a display test looks wrong, let its countdown expire. Use **Restore last**
from Sherpa or its tray menu when you need the emergency saved layout. If necessary,
use Windows **Win+P** or Display Settings to recover the desktop. Review every
confirmation after changing a monitor, cable, port, or display driver.

Closing the window may leave Sherpa in the notification area, depending on Settings.
Exit from the tray when you need to shut it down completely.

Before upgrading, exit Sherpa and back up `%APPDATA%\SherpaManager`. Install the
new version in the same location. Uninstall preserves profiles; reinstalling will
reuse them. The portable build also stores profiles in AppData, not beside its EXE.
Logs live under `%LOCALAPPDATA%\SherpaManager`.

For a bug report, use **Copy diagnostics**, review the copied text, and include it
with your Windows version, GPU/driver, monitor setup, and steps to reproduce in
[an issue](https://github.com/jaksdhflkjahsdfkjahsdfkjh/SherpaManager/issues).
See the [full guide](https://github.com/jaksdhflkjahsdfkjahsdfkjh/SherpaManager#readme)
for Surround, shortcuts, recovery, and known limitations.
