# Generates Microsoft Store / MSIX tile assets from the ClipShot logo.
# Requires ImageMagick (magick.exe). Run after build-branding.ps1 or alone.

$ErrorActionPreference = "Stop"

$brandingDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Resolve-Path (Join-Path $brandingDir "..\..")
$outDir = Join-Path $repoRoot "src\ClipShot.Package\Images"
$svg = Join-Path $brandingDir "ClipShot_Logo.svg"
if (-not (Test-Path $svg)) {
    $svg = Join-Path $brandingDir "clipshot-icon.svg"
}
if (-not (Test-Path $svg)) {
    throw "ClipShot logo SVG not found in assets/branding."
}
if (-not (Get-Command magick.exe -ErrorAction SilentlyContinue)) {
    throw "ImageMagick (magick.exe) is required to generate Store assets."
}

New-Item -ItemType Directory -Force -Path $outDir | Out-Null

function New-Asset([string]$Name, [int]$Width, [int]$Height, [string]$Background = "#F2F3F5") {
    $dest = Join-Path $outDir $Name
    $pad = [Math]::Max(2, [int]([Math]::Min($Width, $Height) * 0.18))
    $innerW = [Math]::Max(1, $Width - (2 * $pad))
    $innerH = [Math]::Max(1, $Height - (2 * $pad))
    # Quote ImageMagick parentheses so PowerShell does not treat them as operators.
    & magick.exe -size "${Width}x${Height}" "xc:$Background" `
        "(" -background none -density 1200 $svg -resize "${innerW}x${innerH}" ")" `
        -gravity center -compose over -composite -strip $dest
    if ($LASTEXITCODE -ne 0) { throw "Failed to generate $Name" }
    Write-Host "  $dest"
}

Write-Host "Generating Store assets into $outDir"
New-Asset "StoreLogo.png" 50 50
New-Asset "Square44x44Logo.png" 44 44
New-Asset "SmallTile.png" 71 71
New-Asset "Square150x150Logo.png" 150 150
New-Asset "LargeTile.png" 310 310
New-Asset "WideTile.png" 310 150
New-Asset "SplashScreen.png" 620 300 "#F2F3F5"
Write-Host "Store assets ready."
