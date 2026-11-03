# ClipShot installer for Windows
# Usage:
#   irm https://raw.githubusercontent.com/niimbu/clipshot/main/install.ps1 | iex

$ErrorActionPreference = "Stop"
$repo = "niimbu/clipshot"
$installDir = Join-Path $env:LOCALAPPDATA "Programs\ClipShot"
$shortcutPath = Join-Path ([Environment]::GetFolderPath("Programs")) "ClipShot.lnk"
$apiUrl = "https://api.github.com/repos/$repo/releases/latest"

Write-Host "Finding the latest ClipShot release..." -ForegroundColor Cyan
try {
    $release = Invoke-RestMethod -Uri $apiUrl -Headers @{ "User-Agent" = "ClipShot-Installer" }
} catch {
    throw "Couldn't retrieve the latest ClipShot release. Please try again later. Details: $($_.Exception.Message)"
}

$asset = $release.assets | Where-Object { $_.name -eq "ClipShot.exe" } | Select-Object -First 1
if (-not $asset) {
    throw "The latest release does not contain ClipShot.exe yet. Please check https://github.com/$repo/releases"
}

New-Item -ItemType Directory -Force -Path $installDir | Out-Null
$exePath = Join-Path $installDir "ClipShot.exe"

Write-Host "Downloading ClipShot $($release.tag_name)..." -ForegroundColor Cyan
Invoke-WebRequest -Uri $asset.browser_download_url -OutFile $exePath

if (-not (Test-Path $exePath)) {
    throw "The download did not produce ClipShot.exe."
}

$wsh = New-Object -ComObject WScript.Shell
$shortcut = $wsh.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $exePath
$shortcut.WorkingDirectory = $installDir
$shortcut.Description = "Capture and share screenshots with ClipShot"
$shortcut.Save()

Write-Host ""
Write-Host "ClipShot $($release.tag_name) was installed successfully." -ForegroundColor Green
Write-Host "You can launch it from the Start Menu or run: $exePath"
