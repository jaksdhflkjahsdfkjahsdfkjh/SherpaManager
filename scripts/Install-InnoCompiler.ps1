[CmdletBinding()]
param([Parameter(Mandatory)] [string] $ToolsDirectory)

$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath($ToolsDirectory)
New-Item -ItemType Directory -Force -Path $root | Out-Null
$setup = Join-Path $root 'innosetup-6.7.3.exe'
$expectedHash = '9c73c3bae7ed48d44112a0f48e66742c00090bdb5bef71d9d3c056c66e97b732'
if (-not (Test-Path -LiteralPath $setup)) {
    Invoke-WebRequest -UseBasicParsing -Uri 'https://github.com/jrsoftware/issrc/releases/download/is-6_7_3/innosetup-6.7.3.exe' -OutFile $setup
}
if ((Get-FileHash -LiteralPath $setup -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expectedHash) {
    throw 'The Inno Setup download does not match the pinned SHA-256.'
}
if ((Get-AuthenticodeSignature -LiteralPath $setup).Status -ne 'Valid') {
    throw 'The Inno Setup download does not have a valid Authenticode signature.'
}
$destination = Join-Path $root 'InnoSetup'
$process = Start-Process -FilePath $setup -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-', '/CURRENTUSER', '/NOICONS', ('/DIR="{0}"' -f $destination)) -WindowStyle Hidden -PassThru -Wait
if ($process.ExitCode -ne 0) { throw "Inno Setup installation failed: $($process.ExitCode)." }
$compiler = Join-Path $destination 'ISCC.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw "Compiler missing at $compiler." }
$compiler
