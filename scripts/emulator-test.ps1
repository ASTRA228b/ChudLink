param([string]$Serial = 'emulator-5554', [switch]$Install, [switch]$RetryCode)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Set-Location -LiteralPath $root
$adb = Join-Path $root '.tools/android-sdk/platform-tools/adb.exe'
$apk = Join-Path $root 'src/ChudLink.Android/bin/Emulator/net10.0-android/ai.chud.chudlink-Signed.apk'
if ($Install) { & $adb -s $Serial install --no-incremental -r $apk; if ($LASTEXITCODE -ne 0) { throw 'APK install failed' } }
$component = (& $adb -s $Serial shell cmd package resolve-activity --brief ai.chud.chudlink | Select-Object -Last 1).Trim()
& $adb -s $Serial shell am start -W -n $component | Out-Null
function Get-Ui {
    & $adb -s $Serial shell uiautomator dump /sdcard/chudlink-test.xml | Out-Null
    $xml = & $adb -s $Serial shell cat /sdcard/chudlink-test.xml
    return [xml]($xml -join "`n")
}
function Tap-Node($node) {
    $numbers = [regex]::Matches($node.bounds, '\d+') | ForEach-Object { [int]$_.Value }
    if ($numbers.Count -ne 4) { throw 'Missing node bounds' }
    $x = [int](($numbers[0]+$numbers[2])/2); $y = [int](($numbers[1]+$numbers[3])/2)
    & $adb -s $Serial shell input tap $x $y
}
$setup = Get-Content -LiteralPath 'artifacts/emulator-setup.json' -Raw | ConvertFrom-Json
$ui = Get-Ui
$fields = $ui.SelectNodes('//node[@package="ai.chud.chudlink" and @class="android.widget.EditText"]')
if ($fields.Count -ne 3) { throw 'Connection screen must be visible. Forget the test host first if already paired.' }
$values = @($setup.address,$setup.fingerprint,$setup.code)
for ($i=$(if ($RetryCode) { 2 } else { 0 }); $i -lt 3; $i++) {
    $ui = Get-Ui
    $fields = $ui.SelectNodes('//node[@package="ai.chud.chudlink" and @class="android.widget.EditText"]')
    Tap-Node $fields[$i]
    & $adb -s $Serial shell input keyevent 123
    $deletes = @('67') * [Math]::Min(200, $fields[$i].text.Length + 2)
    & $adb -s $Serial shell input keyevent @deletes
    if ($i -eq 2) {
        New-Item -ItemType File -Force 'artifacts/emulator-new-code' | Out-Null
        $end = (Get-Date).AddSeconds(5)
        while ((Test-Path 'artifacts/emulator-new-code') -and (Get-Date) -lt $end) { Start-Sleep -Milliseconds 100 }
        $values[2] = (Get-Content -LiteralPath 'artifacts/emulator-setup.json' -Raw | ConvertFrom-Json).code
    }
    & $adb -s $Serial shell input text $values[$i]
    & $adb -s $Serial shell input keyevent 4
}
$ui = Get-Ui
$pair = $ui.SelectSingleNode('//node[@package="ai.chud.chudlink" and @text="PAIR WITH PC"]')
if (-not $pair) { throw 'Pair button not found' }
Tap-Node $pair
Write-Host 'Submitted emulator pairing. Check artifacts/emulator-host-status.json and capture a screenshot after approval.'
