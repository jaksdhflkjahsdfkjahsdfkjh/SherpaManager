# Sherpa Manager

**Switch your PC between work and sim racing with one click.**

Sherpa Manager remembers your monitors, audio devices, and companion apps for each setup. Part of [GridSherpa](https://grid-sherpa.com/), with profiles for **Work**, **iRacing**, and **ACC** to get you started.

## Download and install

1. Open the [Downloads page](https://github.com/jaksdhflkjahsdfkjahsdfkjh/SherpaManager/releases).
2. Download the file ending in **`win-x64-setup.exe`**.
3. Run the installer, then open **Sherpa Manager** from the Start menu.

Requires **64-bit Windows 10 or Windows 11**. The installer includes everything needed to run Sherpa. NVIDIA Surround features require a compatible NVIDIA graphics card and driver.

Prefer no installation? Download **`win-x64-portable.exe`** and run it from a permanent folder. Both versions use the same saved profiles. Exit the old Sherpa tray copy before switching versions.

Downloads are currently unsigned, so Windows may show a SmartScreen warning. Get Sherpa from the Downloads page above.

## Set up your profiles

1. Arrange your monitors in **Windows display settings**. For Surround, set up the monitor group in **NVIDIA settings** first.
2. In Sherpa, select **Work**, **iRacing**, or **ACC**, or create your own profile.
3. Choose **Capture current** to save that monitor setup. Capturing does not change your displays; replacing a saved setup asks for confirmation.
4. Choose **Test**. If everything looks right, select **Keep layout**. Otherwise, wait ten seconds for the previous layout to return.
5. Choose your speakers/headphones and microphone, then use **Add app** to select the programs you want for this profile.
6. Repeat for your other setup. Use **Activate profile** whenever you want to switch.

Your changes save automatically. On smaller windows, use the **Display**, **Audio & shortcuts**, and **Applications** sections. **Previous/Next** reveals content that cannot fit.

**Before switching, save your work.** Apps with **Close on switch** enabled may be force-closed if they do not exit normally. Turn this option off for apps you want to keep open.

## Make switching easier

- **Keyboard shortcut:** choose **Set shortcut** in a profile, then press the keys you want.
- **Desktop shortcut:** choose **Desktop shortcut** to create a shortcut for that profile.
- **Start with Windows:** enable it in **Settings**. Enable **Start minimized** underneath to start quietly when you sign in.
- **Tray or taskbar:** a minimized Windows startup uses the tray when **Keep Sherpa running in the tray when I close its window** is enabled, and the taskbar otherwise. Opening Sherpa yourself still shows its window.
- **Exit completely:** right-click Sherpa's icon in the notification area beside the clock and choose **Exit**.

## If a display switch looks wrong

Let the ten-second countdown expire, or choose **Revert now**. You can also use **Restore last** from Sherpa or its tray menu.

If you cannot see Sherpa, press **Win+P** or open Windows Display Settings to recover your desktop. After changing monitors, cables, or graphics drivers, capture and test your profiles again. Saved layouts belong to the PC where they were captured.

## Updates, backups, and help

To update, exit Sherpa from the tray and install the new version in the same location. Existing v0.7.2 profiles work with v1.0.0. Uninstalling keeps your profiles for a future reinstall.

To back up your profiles, press **Win+R**, enter `%APPDATA%\SherpaManager`, and copy that folder somewhere safe. The portable version uses this folder too.

Found a problem? Open **Settings → Copy diagnostics**, review the copied text, and include it with your steps in [a bug report](https://github.com/jaksdhflkjahsdfkjahsdfkjh/SherpaManager/issues). Sherpa has no accounts or telemetry and does not upload diagnostics automatically.

[Getting started guide](docs/QUICKSTART.md) · [What's new](CHANGELOG.md) · [Contributing](CONTRIBUTING.md) · [Security](SECURITY.md)

Available under the [MIT License](LICENSE). Sherpa Manager is independent and is not affiliated with or endorsed by iRacing or Assetto Corsa Competizione.
