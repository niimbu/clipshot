# ClipShot installer for Windows
# Usage:
#   irm https://raw.githubusercontent.com/niimbu/clipshot/main/install.ps1 | iex

$ErrorActionPreference = "Stop"
$repo = "niimbu/clipshot"
$installDir = Join-Path $env:LOCALAPPDATA "Programs\ClipShot"
$shortcutPath = Join-Path ([Environment]::GetFolderPath("Programs")) "ClipShot.lnk"
$apiUrl = "https://api.github.com/repos/$repo/releases/latest"
$exePath = Join-Path $installDir "ClipShot.exe"
$uninstallScript = Join-Path $installDir "uninstall.ps1"
$uninstallKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\ClipShot"

function Stop-ClipShotProcesses {
    $running = @(Get-Process -Name "ClipShot" -ErrorAction SilentlyContinue)
    if ($running.Count -eq 0) { return }

    # Closing the window only hides ClipShot to the tray, so force-stop the process.
    Write-Host "ClipShot is running — stopping it so the install can update the file..." -ForegroundColor Yellow
    $running | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 500

    $deadline = (Get-Date).AddSeconds(10)
    while ((Get-Date) -lt $deadline) {
        if (-not (Get-Process -Name "ClipShot" -ErrorAction SilentlyContinue)) { return }
        Start-Sleep -Milliseconds 250
    }

    throw "ClipShot is still running and locking $exePath. Open Task Manager, end ClipShot, then run the installer again."
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

$version = ($release.tag_name -replace '^v', '')

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

@'
# ClipShot uninstaller
$ErrorActionPreference = "Stop"
$installDir = Join-Path $env:LOCALAPPDATA "Programs\ClipShot"
$shortcutPath = Join-Path ([Environment]::GetFolderPath("Programs")) "ClipShot.lnk"
$uninstallKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\ClipShot"
$runKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"
Get-Process -Name "ClipShot" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 400
if (Test-Path $shortcutPath) { Remove-Item -LiteralPath $shortcutPath -Force -ErrorAction SilentlyContinue }
if (Test-Path $runKey) { Remove-ItemProperty -LiteralPath $runKey -Name "ClipShot" -ErrorAction SilentlyContinue }
if (Test-Path $uninstallKey) { Remove-Item -LiteralPath $uninstallKey -Recurse -Force -ErrorAction SilentlyContinue }
if (Test-Path $installDir) { Remove-Item -LiteralPath $installDir -Recurse -Force -ErrorAction SilentlyContinue }
'@ | Set-Content -LiteralPath $uninstallScript -Encoding UTF8

New-Item -Path $uninstallKey -Force | Out-Null
Set-ItemProperty -LiteralPath $uninstallKey -Name "DisplayName" -Value "ClipShot"
Set-ItemProperty -LiteralPath $uninstallKey -Name "DisplayVersion" -Value $version
Set-ItemProperty -LiteralPath $uninstallKey -Name "Publisher" -Value "niimbu"
Set-ItemProperty -LiteralPath $uninstallKey -Name "InstallLocation" -Value $installDir
Set-ItemProperty -LiteralPath $uninstallKey -Name "DisplayIcon" -Value $exePath
Set-ItemProperty -LiteralPath $uninstallKey -Name "UninstallString" -Value "powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$uninstallScript`""
Set-ItemProperty -LiteralPath $uninstallKey -Name "QuietUninstallString" -Value "powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$uninstallScript`""
Set-ItemProperty -LiteralPath $uninstallKey -Name "NoModify" -Value 1 -Type DWord
Set-ItemProperty -LiteralPath $uninstallKey -Name "NoRepair" -Value 1 -Type DWord

Write-Host ""
Write-Host "ClipShot $($release.tag_name) was installed successfully." -ForegroundColor Green
Write-Host "Installed to: $exePath"
Write-Host "It also appears under Settings → Apps as ClipShot."
Write-Host ""
Write-Host "Starting ClipShot..." -ForegroundColor Cyan
Start-Process -FilePath $exePath
Write-Host ""
Write-Host "If Windows shows 'Windows protected your PC':" -ForegroundColor Yellow
Write-Host "  1. Click More info"
Write-Host "  2. Click Run anyway"
Write-Host "This is normal for a new open-source app until code signing builds reputation."
