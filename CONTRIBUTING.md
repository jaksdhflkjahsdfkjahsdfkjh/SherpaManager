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

## Code style

- Preserve nullable reference type annotations.
- Prefer safe, reversible behavior. Sherpa must not force-kill an application without explicit per-app user opt-in.
- Keep Windows interop isolated in services.
- Avoid adding dependencies when the Windows or .NET APIs already cover the requirement cleanly.

By contributing, you agree that your contribution will be licensed under the MIT License.
