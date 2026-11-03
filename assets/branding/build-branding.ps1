$ErrorActionPreference = "Stop"

$brandingDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$svg = Join-Path $brandingDir "clipshot-icon.svg"
$png = Join-Path $brandingDir "clipshot-icon.png"
$ico = Join-Path $brandingDir "clipshot.ico"

if (-not (Test-Path $svg)) {
    throw "The ClipShot source logo was not found: $svg"
}

if (-not (Get-Command magick.exe -ErrorAction SilentlyContinue)) {
    throw "ImageMagick is required to export the ClipShot logo. Install ImageMagick and run this script again."
}

& magick.exe -background none $svg -resize 128x128 -strip -define png:compression-level=9 $png
if ($LASTEXITCODE -ne 0) { throw "Failed to export clipshot-icon.png." }

& magick.exe -background none $svg -define icon:auto-resize=48,32,16 $ico
if ($LASTEXITCODE -ne 0) { throw "Failed to export clipshot.ico." }

Write-Host "Generated ClipShot branding assets:"
Write-Host "  $png"
Write-Host "  $ico"
