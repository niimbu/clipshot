$ErrorActionPreference = "Stop"

$brandingDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$svg = Join-Path $brandingDir "ClipShot_Logo.svg"
if (-not (Test-Path $svg)) {
    $svg = Join-Path $brandingDir "clipshot-icon.svg"
}
$png = Join-Path $brandingDir "clipshot-icon.png"
$ico = Join-Path $brandingDir "clipshot.ico"

if (-not (Test-Path $svg)) {
    throw "The ClipShot source logo was not found. Place ClipShot_Logo.svg in assets/branding/."
}

if (-not (Get-Command magick.exe -ErrorAction SilentlyContinue)) {
    throw "ImageMagick is required to export the ClipShot logo. Install ImageMagick and run this script again."
}

# Render the SVG at high density so the brand mark stays sharp in icons and the app chrome.
& magick.exe -background none -density 1200 $svg -resize 1024x1024 -strip -define png:compression-level=9 $png
if ($LASTEXITCODE -ne 0) { throw "Failed to export clipshot-icon.png." }

& magick.exe -background none -density 1200 $svg -define icon:auto-resize=256,128,64,48,32,16 $ico
if ($LASTEXITCODE -ne 0) { throw "Failed to export clipshot.ico." }

Write-Host "Generated ClipShot branding assets from $(Split-Path -Leaf $svg):"
Write-Host "  $png"
Write-Host "  $ico"
