# Contributing to Sherpa Manager

Thanks for helping improve Sherpa Manager. Bug reports, focused feature proposals, documentation improvements, and code contributions are welcome.

## Development setup

You need Windows 10 or 11 and the .NET 8 SDK.

```powershell
dotnet restore SherpaManager.sln
dotnet build SherpaManager.sln -c Release
dotnet run --project tests/SherpaManager.Tests/SherpaManager.Tests.csproj -c Release --no-build
dotnet run --project src/SherpaManager.csproj
```

## Before opening a pull request

1. Keep the change focused and explain its user-facing effect.
2. Build the solution in Release configuration with no warnings or errors.
3. Run the automated test executable and test the affected workflow manually.
4. Describe the monitor and GPU configuration used when changing display code.
5. Do not commit anything from `build/`, `bin/`, `obj/`, or `.vs/`.

Display restoration affects active Windows display topology. Test it carefully and make sure **Win+P** or Windows Display Settings remains available as a recovery path.

Hardware-changing display tests are excluded from normal runs. On a local machine where reapplying the current display topology is safe, the same-topology commit and rejected-transaction rollback tests can be enabled with:

```powershell
$env:SHERPA_HARDWARE_TESTS = "1"
dotnet run --project tests/SherpaManager.Tests/SherpaManager.Tests.csproj -c Release
Remove-Item Env:\SHERPA_HARDWARE_TESTS
```

## Releasing

Releases are cut from tags. The release workflow refuses to run if the tag and the project version disagree, so bump them together.

1. Update `Version`, `AssemblyVersion`, `FileVersion`, and `InformationalVersion` in [Directory.Build.props](Directory.Build.props). This is the only place the version is declared.
2. Move the `Unreleased` entries in [CHANGELOG.md](CHANGELOG.md) under a new `## <version>` heading. The release notes are extracted from that section verbatim.
3. Build and test in Release configuration with no warnings or errors.
4. Push the commit and let it merge to `main` **before** tagging. GitHub runs a workflow as it exists at the tagged commit, so tagging a commit that predates a workflow change silently does nothing.
5. Tag the merged commit `v<version>` and push the tag.

[.github/workflows/release.yml](.github/workflows/release.yml) verifies the version fields and release notes, builds, tests, and packages all four Windows downloads. It checks packaged startup and installer upgrade/uninstall behavior, includes the first-run guide, and writes `SHA256SUMS.txt`. A tag creates a **stable draft** GitHub release; review its files and publish it from the Releases page.

To package without releasing, run the workflow manually from the Actions tab and supply a version. Manual runs upload the archives as workflow artifacts and do not create a release.

For a local installer, publish the self-contained build, then open the script in
Inno Setup and choose Compile. The script reads the published version and checks
it against `Directory.Build.props`; no manual `AppVersion` argument is needed.

```powershell
dotnet publish src/SherpaManager.csproj -c Release -r win-x64 --self-contained true -o build/publish/self-contained
```

Compile `installer/SherpaManager.iss`; the installer is written to `build/artifacts`.
For all four downloads, use `scripts/New-ReleaseArtifacts.ps1` with a fresh publish
root containing `framework-dependent`, `self-contained`, and `portable` builds,
an empty output directory, and the Inno compiler path, as shown in the workflow.

`--smoke-test` loads embedded assets and compiled dialogs and round-trips default
profiles in a disposable directory. It never loads personal profiles or activates
display/audio settings. It does not test the main window, installation, or hardware
recovery. Real display behavior must be verified on the target hardware.

## Code style

- Preserve nullable reference type annotations.
- Prefer safe, reversible behavior. For an enabled application whose **Close on switch** option is enabled, Sherpa requests a normal exit first and automatically force-terminates the matching process tree only if it remains running. Preserve the per-app ability to disable **Close on switch**, and do not broaden process matching or bypass the graceful-close attempt.
- Keep Windows interop isolated in services.
- Avoid adding dependencies when the Windows or .NET APIs already cover the requirement cleanly.

By contributing, you agree that your contribution will be licensed under the MIT License.
