# Sherpa Manager UI

The v0.7 interface follows [GridSherpa](https://grid-sherpa.com/). Its dark palette
was checked against the site's published stylesheet on 2026-09-11. Theme resources
live in `src/App.xaml`; windows explicitly apply the shared Window style.

| Role | Color |
| --- | --- |
| Window | `#131218` |
| Card / sidebar | `#1B1A22` |
| Secondary surface | `#201F28` |
| Divider | `#2C2B35` |
| Main text | `#F0EFF5` |
| Secondary text | `#9B98A8` |
| Accent / focus | `#9B8DFF` |
| Primary action | `#6C57FF` |
| Primary hover | `#5840FF` |
| Caution | `#F0B649` |

Use the shared brushes instead of introducing colors in individual windows. Input
borders use `ControlBorderBrush` for stronger separation from dark surfaces.
`Card`, `SectionTitle`, `SectionIcon`, `PrimaryButton`, and `NavigationButton` define
the common visual treatment. The compact main window uses 20-unit page gutters,
16-unit card padding, and 12–16-unit card gaps. Dialogs use 24-unit page gutters.
Buttons and dropdowns have a 36-unit minimum height.

Icons are native WPF Geometry resources in a 24-unit coordinate space. Set
`ui:Icon.Data` on a button and retain a text label; the shared template renders the
icon in the button's foreground color. Icon-only actions need an accessible name
and tooltip. Keep focus indicators on all interactive controls.

The main window has a 1200×880 minimum size. Its editor and settings use fixed
two-column layouts without page scrolling. The display card spans both audio and
quick-switching rows, so their outer edges align. The application table receives
the remaining height and owns the only visible scrollbars.

`PagedContent` provides Previous/Next actions for overflowing dialog content and
profile lists. It pages a ListBox's own viewport to retain virtualization and
keyboard selection. `AdaptiveColumns` lays out activation-preview sections using
two columns when each can be at least 360 units wide.

The iRacing profile uses the user-supplied `iRacing-Stacked-Color-Blue.png`, whole and
unchanged, directly on the sidebar with no badge behind it; the file is already
transparent. Work uses a vector suitcase and other profiles a grid, drawn in the same
36 px box as the logo. The logo is very nearly square (973 by 924), so the icons match
on width, which is what reads as size in a column. Its dark-blue wordmark is faint on the
charcoal surface at this size, at about 2:1 contrast.

ACC uses the supplied `ACC-logo.png`, whole and transparent, at the same 36 px width. The
logo is a wordmark 3.17 times as wide as it is tall, so at that width it renders 36×11:
the white slashes read and the words do not. That was chosen over cropping to the emblem
(which has no clean boundary with the wordmark) or widening the ACC row alone. A profile
named "ACC" or "Assetto Corsa Competizione" gets it; near misses such as "ACC League"
keep the generic glyph. Both logos must be listed as `Resource` items in the project file;
a missing entry still parses and silently draws nothing, which the layout checks catch.
`ProfileKindConverter` matches names case-insensitively; no profile schema change or
machine-local asset path is needed.
The drag lock uses explicit Locked/Unlocked labels and a filled violet active state.

## Visual verification

Build the solution, then run the layout checks with optional PNG output:

```powershell
dotnet build SherpaManager.sln -c Release
$env:SHERPA_TEST_FILTER = 'UI layout'
$env:SHERPA_UI_SNAPSHOTS = "$PWD\build\ui\screenshots"
dotnet run --project tests/SherpaManager.Tests/SherpaManager.Tests.csproj -c Release --no-build
Remove-Item Env:SHERPA_TEST_FILTER, Env:SHERPA_UI_SNAPSHOTS
```

The window opens at 1240×956, tall enough for five application rows. The display summary
box is a fixed two lines, so the table keeps the same height whether or not a profile has
a captured layout. A check reads the size from the markup itself and fails if a profile
with a full two-line summary shows fewer than five rows, or if the table moves when the
summary changes. The compact checks render
the shipped editor markup at 1240×900 and 1200×880, verify that
controls stay within the page, overview cards align, warnings and long descriptions
do not displace the table, and dialog paging retains virtualization. They also
check the capture-confirmation footer and display rollback countdown. The editor preview
uses sample data with event handlers removed, so it never loads personal profiles,
registers hotkeys, or activates a monitor layout. PNGs are local artifacts under
the ignored `build/` directory.

Also inspect keyboard focus, dropdown navigation, grid editing, and scrolling in
the desktop app when changing templates. Off-screen layout checks do not replace
testing Windows display scaling or a real monitor switch.
