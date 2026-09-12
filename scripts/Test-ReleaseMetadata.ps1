[CmdletBinding()]
param(
    [string] $ExpectedVersion,
    [string] $PropsPath = (Join-Path $PSScriptRoot '..\Directory.Build.props'),
    [string] $ChangelogPath = (Join-Path $PSScriptRoot '..\CHANGELOG.md')
)

$ErrorActionPreference = 'Stop'
$props = [xml](Get-Content -LiteralPath $PropsPath -Raw)
$version = [string]($props.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1)
if ($version -notmatch '^\d+\.\d+\.\d+(\.\d+)?$') { throw "Invalid release version '$version'." }
if ($ExpectedVersion -and $ExpectedVersion -ne $version) { throw "Release version '$ExpectedVersion' does not match project version '$version'." }
$assemblyVersion = if ($version.Split('.').Count -eq 3) { "$version.0" } else { $version }
foreach ($field in @('AssemblyVersion', 'FileVersion', 'InformationalVersion')) {
    $actual = [string]($props.Project.PropertyGroup.$field | Where-Object { $_ } | Select-Object -First 1)
    $expected = if ($field -eq 'InformationalVersion') { $version } else { $assemblyVersion }
    if ($actual -ne $expected) { throw "$field is '$actual'; expected '$expected'." }
}
$changelog = Get-Content -LiteralPath $ChangelogPath -Raw -Encoding UTF8
$pattern = '(?ms)^##[ \t]+' + [regex]::Escape($version) + '[ \t]*\r?$(.*?)(?=^##[ \t]|\z)'
$section = [regex]::Match($changelog, $pattern)
if (-not $section.Success -or [string]::IsNullOrWhiteSpace($section.Groups[1].Value)) {
    throw "CHANGELOG.md must contain nonempty release notes under '## $version'."
}
[pscustomobject]@{ Version = $version; AssemblyVersion = $assemblyVersion; Notes = $section.Groups[1].Value.Trim() }
