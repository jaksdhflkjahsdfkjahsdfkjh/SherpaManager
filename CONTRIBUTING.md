# Contributing to Sherpa Manager

Thanks for helping improve Sherpa Manager. Bug reports, focused feature proposals, documentation improvements, and code contributions are welcome.

## Development setup

You need Windows 10 or 11 and the .NET 8 SDK.

```powershell
dotnet restore SherpaManager.sln
dotnet build SherpaManager.sln -c Release
dotnet run --project src/SherpaManager.csproj
```

## Before opening a pull request

1. Keep the change focused and explain its user-facing effect.
2. Build the solution in Release configuration with no warnings or errors.
3. Test profile persistence and application launching when those areas change.
4. Describe the monitor and GPU configuration used when changing display code.
5. Do not commit anything from `build/`, `bin/`, `obj/`, or `.vs/`.

Display restoration affects active Windows display topology. Test it carefully and make sure **Win+P** or Windows Display Settings remains available as a recovery path.

## Code style

- Preserve nullable reference type annotations.
- Prefer safe, reversible behavior. Sherpa must not force-kill user applications.
- Keep Windows interop isolated in services.
- Avoid adding dependencies when the Windows or .NET APIs already cover the requirement cleanly.

By contributing, you agree that your contribution will be licensed under the MIT License.
