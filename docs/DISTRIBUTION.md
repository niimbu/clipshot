# ClipShot distribution

ClipShot ships as a self-contained Windows x64 app. End users do not need Git, Visual Studio, or the .NET SDK.

## Install

```powershell
irm https://raw.githubusercontent.com/niimbu/clipshot/main/install.ps1 | iex
```

The installer places `ClipShot.exe` in `%LOCALAPPDATA%\Programs\ClipShot` and creates a Start Menu shortcut.

## Portable

From the [latest release](https://github.com/niimbu/clipshot/releases/latest), download **`ClipShot.zip`** or **`ClipShot.exe`**.

Each release also includes `SHA256SUMS.txt` for verification.

## Publishing a release

Maintainers: bump `<Version>` in `src/ClipShot.App/ClipShot.App.csproj` to match the tag (without the `v`), then:

```powershell
git tag v1.0.0
git push origin v1.0.0
```

GitHub Actions builds and publishes the release assets. Signing and other release details live in [RELEASING.md](RELEASING.md).
