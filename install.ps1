# ClipShot installer for Windows
# Usage:
#   irm https://raw.githubusercontent.com/niimbu/clipshot/main/install.ps1 | iex

$ErrorActionPreference = "Stop"
$repo = "niimbu/clipshot"
$installDir = Join-Path $env:LOCALAPPDATA "Programs\ClipShot"
$shortcutPath = Join-Path ([Environment]::GetFolderPath("Programs")) "ClipShot.lnk"
$apiUrl = "https://api.github.com/repos/$repo/releases/latest"
$exePath = Join-Path $installDir "ClipShot.exe"

function Stop-ClipShotProcesses {
    $running = @(Get-Process -Name "ClipShot" -ErrorAction SilentlyContinue)
    if ($running.Count -eq 0) { return }

    Write-Host "ClipShot is running — closing it so the install can update the file..." -ForegroundColor Yellow
    foreach ($proc in $running) {
        try { $proc.CloseMainWindow() | Out-Null } catch {}
    }

    Start-Sleep -Milliseconds 800
    $stillRunning = @(Get-Process -Name "ClipShot" -ErrorAction SilentlyContinue)
    if ($stillRunning.Count -gt 0) {
        Write-Host "Force-stopping ClipShot..." -ForegroundColor Yellow
        $stillRunning | Stop-Process -Force -ErrorAction SilentlyContinue
        Start-Sleep -Milliseconds 500
    }

    $deadline = (Get-Date).AddSeconds(10)
    while ((Get-Date) -lt $deadline) {
        if (-not (Get-Process -Name "ClipShot" -ErrorAction SilentlyContinue)) { return }
        Start-Sleep -Milliseconds 250
    }

    throw "ClipShot is still running and locking $exePath. Close it from the system tray (right-click → Exit), then run the installer again."
}

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
Stop-ClipShotProcesses

$tempPath = Join-Path $installDir ("ClipShot.download." + [Guid]::NewGuid().ToString("N") + ".exe")
try {
    Write-Host "Downloading ClipShot $($release.tag_name)..." -ForegroundColor Cyan
    Invoke-WebRequest -Uri $asset.browser_download_url -OutFile $tempPath

    if (-not (Test-Path $tempPath)) {
        throw "The download did not produce ClipShot.exe."
    }

    # Clear Mark-of-the-Web so Windows doesn't treat the file as an untrusted browser download.
    try {
        Unblock-File -LiteralPath $tempPath -ErrorAction Stop
    } catch {
        Write-Host "Note: couldn't clear the download mark on ClipShot.exe (continuing)." -ForegroundColor DarkYellow
    }

    Move-Item -LiteralPath $tempPath -Destination $exePath -Force
}
finally {
    if (Test-Path $tempPath) {
        Remove-Item -LiteralPath $tempPath -Force -ErrorAction SilentlyContinue
    }
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
Write-Host ""
Write-Host "If Windows shows 'Windows protected your PC':" -ForegroundColor Yellow
Write-Host "  1. Click More info"
Write-Host "  2. Click Run anyway"
Write-Host "This is normal for a new open-source app until code signing builds reputation."
