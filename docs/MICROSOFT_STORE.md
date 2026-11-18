# Publish ClipShot to the Microsoft Store

Microsoft Store installs are signed by Microsoft after certification, so they avoid the SmartScreen warnings that unsigned GitHub `.exe` downloads can hit. GitHub Releases stay the portable path; the Store is an additional channel.

Partner Center individual developer registration is free for many open-source publishers (confirm current terms when you register).

## What this repo includes

| Path | Purpose |
| --- | --- |
| `src/ClipShot.Package/ClipShot.Package.wapproj` | Windows Application Packaging Project |
| `src/ClipShot.Package/Package.appxmanifest` | Package identity, tiles, `runFullTrust`, startup task |
| `src/ClipShot.Package/Images/` | Store / tile assets |
| `src/ClipShot.App/Properties/PublishProfiles/SelfContainedWin64*.pubxml` | Self-contained payload for the package |
| `assets/branding/build-store-assets.ps1` | Regenerate tile PNGs from the logo SVG |

Packaged builds:

- Use the Store for updates (GitHub update UI is hidden).
- Use the `ClipShotStartup` startup task instead of the Run registry key.

## One-time Partner Center setup

1. Register at [Partner Center](https://partner.microsoft.com/dashboard) and create a Windows app.
2. Reserve the name **ClipShot** (or your chosen Store name).
3. Open **Product management → Product identity** and copy:
   - Package name (`Identity.Name`)
   - Publisher (`Identity.Publisher`, the `CN=…` value)
   - Publisher display name
4. Edit `src/ClipShot.Package/Package.appxmanifest` and replace the placeholders:
   - `Identity Name="ClipShot.niimbu"`
   - `Publisher="CN=ClipShot Temporary Packaging"`
   - `PublisherDisplayName`
5. Keep `Identity Version` aligned with `src/ClipShot.App/ClipShot.App.csproj` `<Version>` as `major.minor.patch.0` (for example `1.0.15` → `1.0.15.0`).

## Build the Store package on Windows

Requirements:

- Visual Studio 2022 with **.NET desktop development** and **Universal Windows Platform development** (Desktop Bridge / Windows Application Packaging Project).
- Windows 10/11 SDK.
- ImageMagick if you regenerate branding (`magick.exe`).

```powershell
# Optional: refresh tile assets
./assets/branding/build-store-assets.ps1

# From Visual Studio: open ClipShot.Package.wapproj, set Release | x64,
# then Project → Publish → Create App Packages… → Microsoft Store
# and follow the wizard (associates the app with Partner Center).
```

Or with MSBuild (after installing the packaging workload):

```powershell
msbuild src\ClipShot.Package\ClipShot.Package.wapproj `
  /p:Configuration=Release `
  /p:Platform=x64 `
  /p:TargetPlatformVersion=10.0.19041.0 `
  /p:UapAppxPackageBuildMode=StoreUpload `
  /p:AppxBundle=Always `
  /p:AppxPackageDir="$PWD\artifacts\msix\"
```

Use a `TargetPlatformVersion` that matches a Windows SDK installed on the machine (CI auto-detects the newest UAP platform).

CI uploads an MSIX/AppxUpload artifact on Windows runners when packaging succeeds (see `.github/workflows/build.yml`). Associate the package with your Partner Center app before the first submission if the wizard has not done that yet.

## Signing

CI builds StoreUpload packages with `AppxPackageSigningEnabled=false`. Microsoft re-signs Store submissions. For local sideload testing, use Visual Studio **Create App Packages** (or enable signing with your own test certificate) after associating the app with Partner Center.

## Submission checklist

- Screenshots of the main window, region overlay, and annotation editor (use `docs/images/` as a starting point).
- Short Store description and privacy statement URL (ClipShot does not upload screenshots; Packaged builds do not call GitHub for updates).
- Age rating questionnaire.
- Test the MSIX on a clean Windows 10/11 x64 machine: capture, annotate, copy, save, tray, startup toggle, hotkeys.

## After the Store listing is live

- Link the Store page from the README when you have a public product URL.
- Keep shipping GitHub Releases for users who prefer a portable `.exe`.
- Bump both the app `<Version>` and the manifest `Identity Version` for each Store submission.
