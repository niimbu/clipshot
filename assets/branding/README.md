# ClipShot branding assets

The supplied master artwork lives here:

- `clipshot-icon.svg` — editable source logo.
- `build-branding.ps1` — exports the WPF dashboard PNG and Windows ICO from the SVG.
- `clipshot-icon.png` and `clipshot.ico` — generated build outputs (not committed).

The WPF dashboard uses the PNG. The executable and window use the ICO. When the SVG changes, rerun the export script so both generated files stay in sync.

## Regenerate locally

Install ImageMagick, then run this from the repository root in PowerShell:

```powershell
.\assets\branding\build-branding.ps1
```

The GitHub Actions build runs the same script before compiling, so end users do not need ImageMagick or any developer tools.
