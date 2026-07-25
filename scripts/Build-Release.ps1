[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$Version
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$artifacts = Join-Path $root 'artifacts'
$portable = Join-Path $artifacts 'portable'
$installer = Join-Path $artifacts 'installer'
$dist = Join-Path $root 'dist'

foreach ($directory in @($portable, $installer, $dist)) {
    if (Test-Path -LiteralPath $directory) { Remove-Item -LiteralPath $directory -Recurse -Force }
    New-Item -ItemType Directory -Path $directory | Out-Null
}

$publishArguments = @(
    'publish', (Join-Path $root 'AutoClicker.csproj'),
    '--configuration', 'Release',
    '--runtime', 'win-x64',
    '--self-contained', 'true',
    '-p:PublishSingleFile=true',
    '-p:IncludeNativeLibrariesForSelfExtract=true',
    '-p:DebugType=None',
    "-p:Version=$Version"
)

& dotnet @publishArguments '--output' $portable
if ($LASTEXITCODE -ne 0) { throw 'Portable publish failed.' }
New-Item -ItemType File -Path (Join-Path $portable 'portable.flag') | Out-Null
Compress-Archive -Path (Join-Path $portable '*') -DestinationPath (Join-Path $dist 'AutoClicker-Portable-x64.zip') -Force

& dotnet @publishArguments '--output' $installer
if ($LASTEXITCODE -ne 0) { throw 'Installer publish failed.' }

$isccCandidates = @(
    (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 7\ISCC.exe'),
    (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe')
)
$iscc = $isccCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if ($null -eq $iscc) {
    throw 'Inno Setup 6.6 or newer is required to build the installer. Install it from https://jrsoftware.org/isinfo.php and run this script again.'
}

& $iscc "/DMyAppVersion=$Version" "/O$dist" (Join-Path $root 'installer\AutoClicker.iss')
if ($LASTEXITCODE -ne 0) { throw 'Installer build failed.' }
