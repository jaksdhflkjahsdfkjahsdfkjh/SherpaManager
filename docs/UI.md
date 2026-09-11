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
the common visual treatment. Use 24-unit page gutters, 20-unit card padding, and
16-unit gaps between cards. Buttons and dropdowns have a 36-unit minimum height.

Icons are native WPF Geometry resources in a 24-unit coordinate space. Set
`ui:Icon.Data` on a button and retain a text label; the shared template renders the
icon in the button's foreground color. Icon-only actions need an accessible name
and tooltip. Keep focus indicators on all interactive controls.

`AdaptiveColumns` gives the overview two equal columns when each can be at least
360 units wide, separated by 16 units. Below that width it stacks its children.
Use wrapping toolbars and flexible form columns, and keep window actions outside
scrolling content. The application grid retains horizontal scrolling for its
editable columns when the window is narrow.

## Visual verification

Build the solution, then run the layout checks with optional PNG output:

```powershell
dotnet build SherpaManager.sln -c Release
$env:SHERPA_TEST_FILTER = 'UI layout'
$env:SHERPA_UI_SNAPSHOTS = "$PWD\build\ui\screenshots"
dotnet run --project tests/SherpaManager.Tests/SherpaManager.Tests.csproj -c Release --no-build
Remove-Item Env:SHERPA_TEST_FILTER, Env:SHERPA_UI_SNAPSHOTS
```

The checks render the shipped editor markup at 1200×880 and 960×640, verify that
controls stay within page width and toolbar actions do not overlap, and check the
picker's compact footer and the display confirmation countdown. The editor preview
uses sample data with event handlers removed, so it never loads personal profiles,
registers hotkeys, or activates a monitor layout. PNGs are local artifacts under
the ignored `build/` directory.

Also inspect keyboard focus, dropdown navigation, grid editing, and scrolling in
the desktop app when changing templates. Off-screen layout checks do not replace
testing Windows display scaling or a real monitor switch.
