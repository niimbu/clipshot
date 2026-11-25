# ClipShot uninstaller for Windows
# Usage:
#   irm https://raw.githubusercontent.com/niimbu/clipshot/main/uninstall.ps1 | iex

$ErrorActionPreference = "Stop"
$installDir = Join-Path $env:LOCALAPPDATA "Programs\ClipShot"
$shortcutPath = Join-Path ([Environment]::GetFolderPath("Programs")) "ClipShot.lnk"
$uninstallKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\ClipShot"
$runKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"

Write-Host "Uninstalling ClipShot..." -ForegroundColor Cyan

Get-Process -Name "ClipShot" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 400

if (Test-Path $shortcutPath) {
    Remove-Item -LiteralPath $shortcutPath -Force -ErrorAction SilentlyContinue
}

if (Test-Path $runKey) {
    Remove-ItemProperty -LiteralPath $runKey -Name "ClipShot" -ErrorAction SilentlyContinue
}

if (Test-Path $uninstallKey) {
    Remove-Item -LiteralPath $uninstallKey -Recurse -Force -ErrorAction SilentlyContinue
}

if (Test-Path $installDir) {
    Remove-Item -LiteralPath $installDir -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host "ClipShot was removed." -ForegroundColor Green
Write-Host "Settings were left in: $(Join-Path $env:APPDATA 'ClipShot')"
