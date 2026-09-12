[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $PublishRoot,
    [Parameter(Mandatory)] [string] $OutputDirectory,
    [Parameter(Mandatory)] [string] $CompilerPath,
    [string] $ExpectedVersion
)

$ErrorActionPreference = 'Stop'
$metadata = & (Join-Path $PSScriptRoot 'Test-ReleaseMetadata.ps1') -ExpectedVersion $ExpectedVersion
$version = $metadata.Version
$root = (Resolve-Path -LiteralPath $PublishRoot).Path
$compiler = (Resolve-Path -LiteralPath $CompilerPath).Path
$output = [IO.Path]::GetFullPath($OutputDirectory)
if ((Test-Path -LiteralPath $output) -and (Get-ChildItem -LiteralPath $output -Force | Select-Object -First 1)) {
    throw 'Use an empty output directory to avoid packaging stale release artifacts.'
}
& (Join-Path $PSScriptRoot 'Test-PublishedBuilds.ps1') -PublishRoot $root -ExpectedVersion $version | Out-Host
New-Item -ItemType Directory -Force -Path $output | Out-Null
$repository = Split-Path -Parent $PSScriptRoot
foreach ($kind in @('framework-dependent', 'self-contained')) {
    Copy-Item -LiteralPath (Join-Path $repository 'LICENSE'), (Join-Path $repository 'docs\QUICKSTART.md') -Destination (Join-Path $root $kind)
}
& $compiler /Q "/DAppVersion=$version" "/DPublishDir=$(Join-Path $root 'self-contained')" "/DArtifactDir=$output" (Join-Path $repository 'installer\SherpaManager.iss')
if ($LASTEXITCODE -ne 0) { throw "ISCC failed with exit code $LASTEXITCODE." }
foreach ($kind in @('framework-dependent', 'self-contained')) {
    $suffix = if ($kind -eq 'self-contained') { '-self-contained' } else { '' }
    Compress-Archive -Path (Join-Path $root "$kind\*") -DestinationPath (Join-Path $output "SherpaManager-v$version-win-x64$suffix.zip")
}
Copy-Item -LiteralPath (Join-Path $root 'portable\SherpaManager.exe') -Destination (Join-Path $output "SherpaManager-v$version-win-x64-portable.exe")
Copy-Item -LiteralPath (Join-Path $root 'SMOKE_TEST_RESULTS.json'), (Join-Path $repository 'docs\QUICKSTART.md') -Destination $output
Get-ChildItem -LiteralPath $output -File | Where-Object { $_.Extension -in '.zip', '.exe' } | Sort-Object Name | ForEach-Object {
    '{0}  {1}' -f (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Name
} | Set-Content -LiteralPath (Join-Path $output 'SHA256SUMS.txt') -Encoding ascii
Write-Host "Release artifacts ready: $output"
