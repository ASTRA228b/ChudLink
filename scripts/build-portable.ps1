param([string[]]$Runtime = @('linux-x64', 'linux-arm64', 'osx-x64', 'osx-arm64'), [switch]$SkipPublish)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
Set-Location -LiteralPath $taskRoot
$taskDotnet = if (Test-Path '.tools/dotnet/dotnet.exe') { Join-Path $taskRoot '.tools/dotnet/dotnet.exe' } else { 'dotnet' }
$taskPython = (Get-Command python -ErrorAction Stop).Source
if (Test-Path '.tools/dotnet/dotnet.exe') { $env:DOTNET_ROOT = Split-Path $taskDotnet -Parent }
$taskOutput = Join-Path $taskRoot 'artifacts/portable-drafts'
New-Item -ItemType Directory -Force -Path $taskOutput | Out-Null
$taskProject = Join-Path $taskRoot 'src/ChudLink.Portable/ChudLink.Portable.csproj'
$taskAssets = Join-Path $taskRoot 'src/ChudLink.Portable/obj/project.assets.json'
$taskPack = @'
import os, pathlib, stat, sys, zipfile
stage, dest = map(pathlib.Path, sys.argv[1:])
with zipfile.ZipFile(dest, "w", zipfile.ZIP_DEFLATED, compresslevel=6) as archive:
    for path in sorted(stage.rglob("*")):
        if not path.is_file():
            continue
        relative = path.relative_to(stage).as_posix()
        entry = zipfile.ZipInfo(relative)
        entry.create_system = 3
        executable = path.name == "ChudLink.Portable" or path.suffix in (".so", ".dylib")
        entry.external_attr = (stat.S_IFREG | (0o755 if executable else 0o644)) << 16
        entry.compress_type = zipfile.ZIP_DEFLATED
        with path.open("rb") as source, archive.open(entry, "w", force_zip64=True) as output:
            while data := source.read(1024 * 1024):
                output.write(data)
'@
foreach ($taskRid in $Runtime) {
    if ($taskRid -notin @('linux-x64', 'linux-arm64', 'osx-x64', 'osx-arm64')) { throw "Unsupported draft runtime: $taskRid" }
    $taskStage = Join-Path $taskOutput $taskRid
    $taskBinary = if ($taskRid.StartsWith('osx-')) { Join-Path $taskStage 'ChudLink.app/Contents/MacOS' } else { $taskStage }
    New-Item -ItemType Directory -Force -Path $taskBinary | Out-Null
    if (-not $SkipPublish) {
        & $taskDotnet publish $taskProject -c Release -r $taskRid --self-contained true -p:PublishTrimmed=false -o $taskBinary
        if ($LASTEXITCODE -ne 0) { throw "Portable draft publish failed: $taskRid" }
    }
    if (-not (Test-Path (Join-Path $taskBinary 'ChudLink.Portable'))) { throw "Missing draft executable: $taskRid" }
    if ($taskRid.StartsWith('osx-')) {
        $taskPlist = @'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
<key>CFBundleName</key><string>ChudLink Draft</string>
<key>CFBundleDisplayName</key><string>ChudLink Draft</string>
<key>CFBundleIdentifier</key><string>ai.chud.chudlink.portable.draft</string>
<key>CFBundleExecutable</key><string>ChudLink.Portable</string>
<key>CFBundlePackageType</key><string>APPL</string>
<key>CFBundleShortVersionString</key><string>0.1.1</string>
<key>CFBundleVersion</key><string>1</string>
<key>NSHighResolutionCapable</key><true/>
<key>NSLocalNetworkUsageDescription</key><string>ChudLink connects to paired Android companions on your local network.</string>
</dict></plist>
'@
        [IO.File]::WriteAllText((Join-Path $taskStage 'ChudLink.app/Contents/Info.plist'), $taskPlist, [Text.UTF8Encoding]::new($false))
    }
    Copy-Item -LiteralPath 'docs/PORTABLE-DRAFTS.md' -Destination (Join-Path $taskStage 'README-DRAFT.md') -Force
    Copy-Item -LiteralPath 'src/ChudLink.Portable/THIRD-PARTY-NOTICES.md' -Destination $taskStage -Force
    $taskNotices = Join-Path $taskStage 'ThirdParty'
    New-Item -ItemType Directory -Force -Path $taskNotices | Out-Null
    $taskMetadata = Get-Content -LiteralPath $taskAssets -Raw | ConvertFrom-Json
    $taskPackageFolder = $taskMetadata.packageFolders.PSObject.Properties.Name | Select-Object -First 1
    foreach ($taskPackage in $taskMetadata.libraries.PSObject.Properties) {
        if ($taskPackage.Value.type -ne 'package') { continue }
        $taskCache = Join-Path $taskPackageFolder $taskPackage.Value.path
        if (-not (Test-Path -LiteralPath $taskCache)) { continue }
        $taskNoticeFolder = Join-Path $taskNotices ($taskPackage.Name.Replace('/', '-'))
        New-Item -ItemType Directory -Force -Path $taskNoticeFolder | Out-Null
        Get-ChildItem -LiteralPath $taskCache -File -Recurse -Depth 2 | Where-Object { $_.Name -match '^(LICENSE|LICENCE|COPYING|NOTICE|THIRD.PARTY|ThirdParty)' -or $_.Extension -eq '.nuspec' } | ForEach-Object {
            $taskNoticeName = $_.FullName.Substring($taskCache.Length).TrimStart('\', '/').Replace('\', '_').Replace('/', '_')
            Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $taskNoticeFolder $taskNoticeName) -Force
        }
    }
    $taskDeps = Get-Content -LiteralPath (Join-Path $taskBinary 'ChudLink.Portable.deps.json') -Raw | ConvertFrom-Json
    foreach ($taskPackEntry in $taskDeps.libraries.PSObject.Properties.Name | Where-Object { $_.StartsWith('runtimepack.') }) {
        $taskPackId, $taskPackVersion = $taskPackEntry.Split('/')
        $taskPackId = $taskPackId.Substring('runtimepack.'.Length).ToLowerInvariant()
        $taskPackCache = Join-Path $taskPackageFolder "$taskPackId/$taskPackVersion"
        if (-not (Test-Path -LiteralPath $taskPackCache)) { throw "Runtime license source is missing: $taskPackEntry" }
        $taskPackNotices = Join-Path $taskNotices "$taskPackId-$taskPackVersion"
        New-Item -ItemType Directory -Force -Path $taskPackNotices | Out-Null
        Get-ChildItem -LiteralPath $taskPackCache -File | Where-Object { $_.Name -match '^(LICENSE|LICENCE|COPYING|NOTICE|THIRD.PARTY)' -or $_.Extension -eq '.nuspec' } | ForEach-Object {
            Copy-Item -LiteralPath $_.FullName -Destination $taskPackNotices -Force
        }
    }
    $taskStatus = @{ product = 'ChudLink'; version = '0.1.1-portable-draft'; runtime = $taskRid; crossPublishedOn = [Runtime.InteropServices.RuntimeInformation]::OSDescription; nativeExecutionTested = $false; nativeGuiTested = $false; signed = $false; notarized = $false; channel = 'draft-only' }
    $taskStatus | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $taskStage 'DRAFT-STATUS.json') -Encoding utf8
    $taskArchive = Join-Path $taskOutput "ChudLink-0.1.1-portable-draft-$taskRid.zip"
    & $taskPython -c $taskPack $taskStage $taskArchive
    if ($LASTEXITCODE -ne 0) { throw "Draft archive creation failed: $taskRid" }
    Get-FileHash -LiteralPath $taskArchive -Algorithm SHA256 | Select-Object Path, Hash
}
