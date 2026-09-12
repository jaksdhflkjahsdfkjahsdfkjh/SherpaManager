[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Installer,
    [Parameter(Mandatory)] [string] $PreviousInstaller,
    [string] $ExpectedVersion = '1.0.0'
)

$ErrorActionPreference = 'Stop'
$installerPath = (Resolve-Path -LiteralPath $Installer).Path
$previousPath = (Resolve-Path -LiteralPath $PreviousInstaller).Path
$uninstallSubkey = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{C6FE855E-2DB5-4B32-902B-82E4366BA7A5}_is1'
$uninstallKey = "HKCU:\$uninstallSubkey"
$startupKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
foreach ($key in @($uninstallKey, "HKLM:\$uninstallSubkey", ('HKLM:\' + $uninstallSubkey.Replace('Software\', 'Software\WOW6432Node\')))) {
    if (Test-Path -LiteralPath $key) { throw 'Installer testing requires an account without an existing Sherpa Manager installation.' }
}
if (Get-ItemProperty -LiteralPath $startupKey -Name SherpaManager -ErrorAction SilentlyContinue) {
    throw 'Installer testing requires an account without a Sherpa Manager startup entry.'
}
$testRoot = Join-Path (Split-Path -Parent $PSScriptRoot) ('build\installer-validation-' + [guid]::NewGuid().ToString('N'))
$installDirectory = Join-Path $testRoot 'Installed app'
New-Item -ItemType Directory -Path $testRoot | Out-Null
$installedCommand = '"' + (Join-Path $installDirectory 'SherpaManager.exe') + '"'
$minimizedCommand = $installedCommand + ' --minimized'
$otherCommand = '"' + (Join-Path $testRoot 'Other copy\SherpaManager.exe') + '"'
$results = @()

function Invoke-TestProcess([string] $File, [string[]] $Arguments) {
    $process = Start-Process -FilePath $File -ArgumentList $Arguments -WindowStyle Hidden -PassThru
    try {
        if (-not $process.WaitForExit(120000)) { throw "Process timed out; inspect the test installation and log at $testRoot before retrying." }
        if ($process.ExitCode -ne 0) { throw "$File failed with exit code $($process.ExitCode)." }
    }
    finally { $process.Dispose() }
}
function Install-TestPackage([string] $File, [string] $LogName) {
    Invoke-TestProcess $File @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-', '/CURRENTUSER', '/NOICONS', ('/DIR="{0}"' -f $installDirectory), ('/LOG="{0}"' -f (Join-Path $testRoot $LogName)))
}
function Uninstall-TestPackage([string] $LogName) {
    $registered = Get-ItemProperty -LiteralPath $uninstallKey -ErrorAction SilentlyContinue
    if (-not $registered) { return }
    if ($registered.InstallLocation.TrimEnd('\') -ne $installDirectory.TrimEnd('\')) {
        throw 'The registered installation location changed; refusing to uninstall another copy.'
    }
    # Inno may increment this name after reinstalling before a previous uninstaller
    # has finished deleting itself. Always use the path registered by this install.
    if ($registered.UninstallString -notmatch '^"([^"]+)"$') { throw 'Unexpected registered uninstall command.' }
    $uninstaller = [IO.Path]::GetFullPath($Matches[1])
    if ((Split-Path -Parent $uninstaller) -ne $installDirectory) { throw 'Uninstaller path is outside the test installation.' }
    Invoke-TestProcess $uninstaller @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', ('/LOG="{0}"' -f (Join-Path $testRoot $LogName)))
}

try {
    Install-TestPackage $previousPath 'install-previous.log'
    if ((Get-ItemProperty -LiteralPath $uninstallKey).DisplayVersion -ne '0.7.2') { throw 'The previous installer is not version 0.7.2.' }
    $sentinel = Join-Path $installDirectory 'user-file.txt'
    Set-Content -LiteralPath $sentinel -Value 'Preserve files not owned by the installer.'
    Install-TestPackage $installerPath 'upgrade.log'
    if ((Get-ItemProperty -LiteralPath $uninstallKey).DisplayVersion -ne $ExpectedVersion) { throw 'The upgrade did not register the expected version.' }
    if (-not (Test-Path -LiteralPath $sentinel)) { throw 'Upgrade removed a user-owned file.' }
    Invoke-TestProcess (Join-Path $installDirectory 'SherpaManager.exe') @('--smoke-test')
    $results += 'PASS upgrade from 0.7.2 and installed package smoke test'

    if (-not (Test-Path -LiteralPath $startupKey)) { New-Item -Path $startupKey | Out-Null }
    New-ItemProperty -LiteralPath $startupKey -Name SherpaManager -Value $installedCommand -PropertyType String | Out-Null
    Uninstall-TestPackage 'uninstall.log'
    if (Test-Path -LiteralPath $uninstallKey) { throw 'Uninstall left its registration behind.' }
    if (Get-ItemProperty -LiteralPath $startupKey -Name SherpaManager -ErrorAction SilentlyContinue) { throw 'Uninstall left its startup entry behind.' }
    if (-not (Test-Path -LiteralPath $sentinel)) { throw 'Uninstall removed a user-owned file.' }
    $results += 'PASS uninstall removes matching startup entry and preserves unowned files'

    Install-TestPackage $installerPath 'minimized-install.log'
    New-ItemProperty -LiteralPath $startupKey -Name SherpaManager -Value $minimizedCommand -PropertyType String | Out-Null
    Uninstall-TestPackage 'uninstall-minimized.log'
    if (Test-Path -LiteralPath $uninstallKey) { throw 'Minimized-startup uninstall left its registration behind.' }
    if (Get-ItemProperty -LiteralPath $startupKey -Name SherpaManager -ErrorAction SilentlyContinue) { throw 'Uninstall left the minimized startup entry behind.' }
    $results += 'PASS uninstall removes the --minimized Windows startup entry'

    Install-TestPackage $installerPath 'clean-install.log'
    Invoke-TestProcess (Join-Path $installDirectory 'SherpaManager.exe') @('--smoke-test')
    New-ItemProperty -LiteralPath $startupKey -Name SherpaManager -Value $otherCommand -PropertyType String | Out-Null
    Uninstall-TestPackage 'uninstall-other-startup.log'
    if (Test-Path -LiteralPath $uninstallKey) { throw 'Second uninstall left its registration behind.' }
    if (Test-Path -LiteralPath (Join-Path $installDirectory 'SherpaManager.exe')) { throw 'Second uninstall left the application installed.' }
    if ((Get-ItemProperty -LiteralPath $startupKey -Name SherpaManager).SherpaManager -ne $otherCommand) { throw 'Uninstall changed a startup entry for another copy.' }
    $results += 'PASS clean install, smoke test, and preservation of another copy startup entry'
    $results | Set-Content -LiteralPath (Join-Path $testRoot 'RESULTS.txt')
    $results | Write-Output
    Write-Output "Installer test logs: $testRoot"
}
finally {
    Uninstall-TestPackage 'cleanup.log'
    $entry = Get-ItemProperty -LiteralPath $startupKey -Name SherpaManager -ErrorAction SilentlyContinue
    if ($entry -and $entry.SherpaManager -in @($installedCommand, $minimizedCommand, $otherCommand)) {
        Remove-ItemProperty -LiteralPath $startupKey -Name SherpaManager
    }
}
