# ClipShot distribution

ClipShot is distributed as a self-contained Windows x64 application by **niimbu**. End users do not need to install Git, Visual Studio, or the .NET SDK.

## Recommended install (avoids browser download warnings)

Chrome and Edge often warn on new or unsigned desktop apps. Prefer the PowerShell installer, which downloads ClipShot outside the browser:

```powershell
irm https://raw.githubusercontent.com/niimbu/clipshot/main/install.ps1 | iex
```

The installer downloads the latest `ClipShot.exe` release asset into `%LOCALAPPDATA%\Programs\ClipShot` and creates a Start Menu shortcut.

## Portable version

From the latest GitHub Release, download **`ClipShot.zip`** (saved as `ClipShot.zip`), extract it, and run `ClipShot.exe`.

Each release also publishes:
- `ClipShot.exe` — direct Windows executable
- `ClipShot.zip` — portable package
- `SHA256SUMS.txt` — SHA-256 hashes for verification

## Chrome “dangerous file” warnings

Unsigned or low-volume apps can still be flagged even when the file is legitimate. Use the PowerShell installer above when possible. If you download from the official [ClipShot Releases](https://github.com/niimbu/clipshot/releases) page and Chrome warns, choose **Download anyway** / **Keep** only after confirming the release URL is correct, then verify the hash in `SHA256SUMS.txt`.

For lasting trust with browsers and SmartScreen, configure Authenticode signing secrets (see [RELEASING.md](RELEASING.md)).

## Publishing a release

Push a version tag such as `v0.1.0`. GitHub Actions builds the app as a self-contained, single-file Windows x64 executable and publishes `ClipShot.exe`, `ClipShot.zip`, and checksums to a GitHub Release.

```powershell
git tag v0.1.0
git push origin v0.1.0
```

The first public release must be created before the one-command installer can download a release asset. Until then, the installer intentionally reports that no release is available.
