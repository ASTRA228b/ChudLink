param([switch]$SkipAndroid, [switch]$SkipTests, [switch]$SelfContained)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Set-Location -LiteralPath $root
$dotnet = if (Test-Path '.tools\dotnet\dotnet.exe') { Join-Path $root '.tools\dotnet\dotnet.exe' } else { 'dotnet' }
$env:DOTNET_ROOT = Split-Path $dotnet -Parent
function Run-Dotnet { & $dotnet @args; if ($LASTEXITCODE -ne 0) { throw "dotnet failed: $args" } }
$selfContainedValue = if ($SelfContained) { 'true' } else { 'false' }
Run-Dotnet build ChudLink.Windows.slnx -c Release
Run-Dotnet publish src/ChudLink.ExtensionHost -c Release -r win-x64 --self-contained $selfContainedValue -o artifacts/worker
Run-Dotnet publish src/ChudLink.Host -c Release -r win-x64 --self-contained $selfContainedValue -o artifacts/host
Run-Dotnet publish src/ChudLink.Desktop -c Release -r win-x64 --self-contained $selfContainedValue -o artifacts/windows
$adbSource = Join-Path $root '.tools/android-sdk/platform-tools'
if (Test-Path (Join-Path $adbSource 'adb.exe')) {
    foreach ($destination in @('artifacts/windows/tools/adb', 'artifacts/host/tools/adb')) {
        New-Item -ItemType Directory -Force -Path $destination | Out-Null
        foreach ($file in @('adb.exe', 'AdbWinApi.dll', 'AdbWinUsbApi.dll', 'NOTICE.txt')) {
            Copy-Item -LiteralPath (Join-Path $adbSource $file) -Destination $destination -Force
        }
    }
} else { Write-Warning 'USB auto-connect tools are absent. Run scripts/bootstrap.ps1 -Android to include them in local builds.' }
foreach ($destination in @('artifacts/windows/worker','artifacts/host/worker','tests/ChudLink.Tests/bin/Release/net10.0-windows/worker')) {
    New-Item -ItemType Directory -Force -Path $destination | Out-Null
    Copy-Item -Path 'artifacts/worker/*' -Destination $destination -Recurse -Force
}
Run-Dotnet pack src/ChudLink.SDK -c Release -o artifacts/sdk
Run-Dotnet pack src/ChudLink.Protocol -c Release -o artifacts/sdk
Run-Dotnet run --project tests/ChudLink.Tests -c Release --no-build -- --pack-examples
if (-not $SkipTests) { Run-Dotnet run --project tests/ChudLink.Tests -c Release --no-build }
if (-not $SkipAndroid) {
    Run-Dotnet build src/ChudLink.Android -c Release
    New-Item -ItemType Directory -Force artifacts/android | Out-Null
    Copy-Item -Path 'src/ChudLink.Android/bin/Release/net10.0-android/*-Signed.apk' -Destination 'artifacts/android/ChudLink-0.1.1-Alpha.apk' -Force
}
Copy-Item -LiteralPath 'docs' -Destination 'artifacts/windows' -Recurse -Force
Copy-Item -LiteralPath 'README.md' -Destination 'artifacts/windows/README.md' -Force
Write-Host 'ChudLink builds are in artifacts/windows, artifacts/host, artifacts/android, artifacts/sdk and artifacts/extensions.'
