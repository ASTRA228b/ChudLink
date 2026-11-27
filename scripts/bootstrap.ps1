param([switch]$Android)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Set-Location -LiteralPath $root
New-Item -ItemType Directory -Force .tools | Out-Null
if (-not (Test-Path '.tools/dotnet/dotnet.exe')) {
    Invoke-WebRequest -Uri 'https://dot.net/v1/dotnet-install.ps1' -OutFile '.tools/dotnet-install.ps1'
    & ./.tools/dotnet-install.ps1 -Version 10.0.404 -InstallDir "$root/.tools/dotnet" -NoPath
    if ($LASTEXITCODE -ne 0) { throw 'SDK install failed' }
}
$dotnet = Join-Path $root '.tools/dotnet/dotnet.exe'
if ($Android) {
    & $dotnet workload install android --skip-manifest-update
    if ($LASTEXITCODE -ne 0) { throw 'Android workload install failed' }
    & $dotnet build src/ChudLink.Android -t:InstallAndroidDependencies -f net10.0-android "-p:AndroidSdkDirectory=$root/.tools/android-sdk" "-p:JavaSdkDirectory=$root/.tools/jdk" -p:AcceptAndroidSdkLicenses=True
    if ($LASTEXITCODE -ne 0) { throw 'Android SDK installation failed' }
}
