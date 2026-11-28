$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
Set-Location -LiteralPath $taskRoot
$taskOutput = Join-Path $taskRoot 'artifacts/release-0.1.1'
New-Item -ItemType Directory -Force -Path $taskOutput | Out-Null
$taskWindows = Join-Path $taskRoot 'artifacts/windows'
$taskApk = Join-Path $taskRoot 'artifacts/android/ChudLink-0.1.1-Alpha.apk'
foreach ($taskFile in @('ChudLink.Desktop.exe','worker/ChudLink.ExtensionHost.exe','tools/adb/adb.exe')) {
    if (-not (Test-Path -LiteralPath (Join-Path $taskWindows $taskFile))) { throw "Incomplete Windows package: $taskFile" }
}
if (-not (Test-Path -LiteralPath $taskApk)) { throw 'Build the 0.1.1 APK first.' }
$taskPrivate = @(Get-ChildItem -LiteralPath $taskWindows -File -Recurse | Where-Object { $_.Extension -in @('.pfx','.keystore') -or $_.Name -in @('devices.json','settings.json','host.lock') -or $_.Name -like '*.local.json' })
if ($taskPrivate.Count -ne 0) { throw 'Private runtime data must not be packaged.' }
Copy-Item -Path 'docs/*' -Destination (Join-Path $taskWindows 'docs') -Recurse -Force
Copy-Item -LiteralPath 'README.md' -Destination $taskWindows -Force
$taskZip = Join-Path $taskOutput 'ChudLink-0.1.1-Alpha-Windows-x64.zip'
if (Test-Path -LiteralPath $taskZip) { Remove-Item -LiteralPath $taskZip }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($taskWindows, $taskZip, [IO.Compression.CompressionLevel]::Optimal, $false)
Copy-Item -LiteralPath $taskApk -Destination $taskOutput -Force
Copy-Item -LiteralPath 'docs/PERFORMANCE.md' -Destination $taskOutput -Force
$taskLines = Get-ChildItem -LiteralPath $taskOutput -File | Where-Object Name -NE 'SHA256SUMS.txt' | Sort-Object Name | ForEach-Object { (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + $_.Name }
[IO.File]::WriteAllLines((Join-Path $taskOutput 'SHA256SUMS.txt'), $taskLines, [Text.UTF8Encoding]::new($false))
$taskArchive = [IO.Compression.ZipFile]::OpenRead($taskZip)
try {
    if ($taskArchive.Entries.FullName -match '(^|/)(host\.pfx|devices\.json|settings\.json|.*\.keystore|.*\.local\.json)$') { throw 'Archive contains private data.' }
    foreach ($taskRequired in @('ChudLink.Desktop.exe','worker/ChudLink.ExtensionHost.exe','tools/adb/adb.exe','docs/PERFORMANCE.md')) {
        if ($taskRequired -notin @($taskArchive.Entries.FullName.Replace('\','/'))) { throw "Archive is missing $taskRequired" }
    }
    [pscustomobject]@{ archive=$taskZip; entries=$taskArchive.Entries.Count; bytes=(Get-Item -LiteralPath $taskZip).Length } | ConvertTo-Json -Compress
} finally { $taskArchive.Dispose() }
