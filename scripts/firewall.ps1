# Optional. Run in an elevated PowerShell after reviewing the selected paths and ports.
param([ValidateSet('Install','Remove')][string]$Action = 'Install')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$rules = @('ChudLink private HTTPS', 'ChudLink private discovery')
if ($Action -eq 'Remove') { foreach ($name in $rules) { Get-NetFirewallRule -DisplayName $name -ErrorAction SilentlyContinue | Remove-NetFirewallRule }; exit }
$app = Join-Path $root 'artifacts/windows/ChudLink.Desktop.exe'
if (-not (Test-Path -LiteralPath $app)) { throw 'Build the desktop release first.' }
New-NetFirewallRule -DisplayName $rules[0] -Direction Inbound -Profile Private -Action Allow -Program $app -Protocol TCP -LocalPort 47831 -RemoteAddress LocalSubnet
New-NetFirewallRule -DisplayName $rules[1] -Direction Inbound -Profile Private -Action Allow -Program $app -Protocol UDP -LocalPort 47832 -RemoteAddress LocalSubnet
