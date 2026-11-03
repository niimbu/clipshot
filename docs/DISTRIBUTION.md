# ClipShot distribution

ClipShot is distributed as a self-contained Windows x64 application. End users do not need to install Git, Visual Studio, or the .NET SDK.

## Install from PowerShell

After the first GitHub Release is published, users can install with:

```powershell
irm https://raw.githubusercontent.com/niimbu/clipshot/main/install.ps1 | iex
```

The installer downloads the latest `ClipShot.exe` release asset into `%LOCALAPPDATA%\Programs\ClipShot` and creates a Start Menu shortcut.

## Portable version

Download `ClipShot-win-x64-portable.zip` from the latest GitHub Release, extract it, and run `ClipShot.exe`.

## Publishing a release

Push a version tag such as `v0.1.0`. GitHub Actions builds the app as a self-contained, single-file Windows x64 executable and publishes the executable plus portable ZIP to a GitHub Release.

Example:

```powershell
git tag v0.1.0
git push origin v0.1.0
```

The first public release must be created before the one-command installer can download a release asset. Until then, the installer intentionally reports that no release is available.
