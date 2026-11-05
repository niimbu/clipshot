# ClipShot branding

Place the official brand mark at `ClipShot_Logo.svg` (the file from the project desktop / design handoff).

Generate Windows assets with ImageMagick:

```powershell
./assets/branding/build-branding.ps1
```

This writes:
- `clipshot-icon.png` — UI and Start Menu imagery
- `clipshot.ico` — application icon

The GitHub Actions Windows build runs this script before publishing.
