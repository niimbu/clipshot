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

# Render the 16x16 SVG directly to a large transparent PNG to avoid upscaling a tiny raster.
& magick.exe -background none -density 1200 $svg -resize 1024x1024 -strip -define png:compression-level=9 $png
if ($LASTEXITCODE -ne 0) { throw "Failed to export clipshot-icon.png." }

& magick.exe -background none -density 1200 $svg -define icon:auto-resize=256,128,64,48,32,16 $ico
if ($LASTEXITCODE -ne 0) { throw "Failed to export clipshot.ico." }

Write-Host "Generated ClipShot branding assets:"
Write-Host "  $png"
Write-Host "  $ico"