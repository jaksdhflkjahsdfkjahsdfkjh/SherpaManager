[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $PublishRoot,
    [string] $ExpectedVersion,
    [int] $TimeoutSeconds = 60
)

$ErrorActionPreference = 'Stop'
if ($TimeoutSeconds -lt 1) { throw 'TimeoutSeconds must be positive.' }
$metadata = & (Join-Path $PSScriptRoot 'Test-ReleaseMetadata.ps1') -ExpectedVersion $ExpectedVersion
$root = (Resolve-Path -LiteralPath $PublishRoot).Path
$results = @()
foreach ($kind in @('framework-dependent', 'self-contained', 'portable')) {
    $executable = Join-Path $root "$kind\SherpaManager.exe"
    if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) { throw "Missing $executable" }
    $bytes = [System.IO.File]::ReadAllBytes($executable)
    if ($bytes.Length -lt 64 -or $bytes[0] -ne 0x4d -or $bytes[1] -ne 0x5a) { throw "Invalid executable: $executable" }
    $peOffset = [BitConverter]::ToInt32($bytes, 0x3c)
    if ($peOffset -lt 0 -or $peOffset -gt $bytes.Length - 6 -or [BitConverter]::ToUInt32($bytes, $peOffset) -ne 0x4550) { throw "Invalid PE header: $executable" }
    if ([BitConverter]::ToUInt16($bytes, $peOffset + 4) -ne 0x8664) { throw "$executable is not x64." }
    $info = [Diagnostics.FileVersionInfo]::GetVersionInfo($executable)
    if ($info.FileVersion -ne $metadata.AssemblyVersion) { throw "$executable has file version '$($info.FileVersion)' instead of '$($metadata.AssemblyVersion)'." }

    $start = New-Object Diagnostics.ProcessStartInfo
    $start.FileName = $executable
    $start.Arguments = '--smoke-test'
    $start.WorkingDirectory = Split-Path -Parent $executable
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
    $process = [Diagnostics.Process]::Start($start)
    try {
        if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
            $process.Kill()
            $process.WaitForExit()
            throw "$kind smoke test timed out after $TimeoutSeconds seconds."
        }
        if ($process.ExitCode -ne 0) { throw "$kind smoke test failed with exit code $($process.ExitCode)." }
        $results += [pscustomobject]@{ Package = $kind; Version = $info.FileVersion; Architecture = 'x64'; SmokeTest = 'passed'; Sha256 = (Get-FileHash -LiteralPath $executable -Algorithm SHA256).Hash.ToLowerInvariant() }
        Write-Host "PASS ${kind}: x64, version $($info.FileVersion), packaged smoke test"
    }
    finally { $process.Dispose() }
}
$results | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $root 'SMOKE_TEST_RESULTS.json') -Encoding UTF8
$results
