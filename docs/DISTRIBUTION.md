# ClipShot distribution

ClipShot ships as a self-contained Windows x64 app. End users do not need Git, Visual Studio, or the .NET SDK.

## Install

```powershell
irm https://raw.githubusercontent.com/niimbu/clipshot/main/install.ps1 | iex
```

The installer places `ClipShot.exe` in `%LOCALAPPDATA%\Programs\ClipShot`, clears the download “Mark of the Web”, and creates a Start Menu shortcut.

## Portable

From the [latest release](https://github.com/niimbu/clipshot/releases/latest), download **`ClipShot.zip`** or **`ClipShot.exe`**.

Each release also includes `SHA256SUMS.txt` for verification.

## If Windows blocks ClipShot

SmartScreen often shows **Windows protected your PC** for new or unsigned apps. ClipShot is safe when downloaded from the official GitHub releases.

1. On the blue/yellow SmartScreen dialog, click **More info**.
2. Click **Run anyway**.

Or install with the PowerShell command above (preferred over browser downloads).

If the file was downloaded in a browser and won’t start:

```powershell
Unblock-File "$env:USERPROFILE\Downloads\ClipShot.exe"
```

(Adjust the path if needed.)

Lasting fix for all users: ship Authenticode-signed builds (see [RELEASING.md](RELEASING.md)).

## Publishing a release

Maintainers: bump `<Version>` in `src/ClipShot.App/ClipShot.App.csproj` to match the tag (without the `v`), then:

```powershell
git tag v1.0.0
git push origin v1.0.0
```

GitHub Actions builds and publishes the release assets. Signing details live in [RELEASING.md](RELEASING.md).
