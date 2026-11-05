# Releasing ClipShot for Windows

## Authenticode signing

The Windows build workflow supports optional Authenticode signing. Signing helps Windows and browser security systems establish the publisher and verify that the executable has not been modified. It does not guarantee that Chrome or Microsoft Defender SmartScreen will stop warning about a new download; reputation can take time to build.

1. Obtain a publicly trusted code-signing certificate from a recognized certificate authority or signing service. A self-signed certificate is not sufficient for public distribution.
2. Export the certificate as a password-protected PFX file, including its private key.
3. Base64-encode the PFX file without changing its contents. For example, in PowerShell:
   ```powershell
   [Convert]::ToBase64String([IO.File]::ReadAllBytes(".\publisher.pfx")) | Set-Clipboard
   ```
4. In the GitHub repository, open **Settings → Secrets and variables → Actions** and add:
   - `WINDOWS_SIGNING_CERTIFICATE_BASE64`: the base64 text from the previous step.
   - `WINDOWS_SIGNING_CERTIFICATE_PASSWORD`: the PFX password.
5. Run the workflow or push a new version tag. The workflow signs and verifies `ClipShot.exe` before creating `ClipShot.zip` and release assets. If the secrets are not configured, the build remains unsigned.

Keep the PFX and password private. Never commit either one to the repository or include them in build artifacts.

## Chrome download warnings

A code signature is an important trust signal, but it cannot instantly create download reputation. New and low-volume applications can still receive warnings. Prefer the PowerShell installer in [DISTRIBUTION.md](DISTRIBUTION.md) so users do not download the ZIP through Chrome.

If Chrome displays a warning for an official release, confirm the download came from the [ClipShot Releases page](https://github.com/niimbu/clipshot/releases). Do not ask users to disable Safe Browsing.

If the official release is incorrectly detected, submit the exact release URL and file to Google's [Safe Browsing incorrect warning reporting](https://safebrowsing.google.com/safebrowsing/report_error/). Include the version and SHA-256 hash from `SHA256SUMS.txt`.

## Versioning

Keep the `<Version>` in `src/ClipShot.App/ClipShot.App.csproj` identical to the Git tag you push (without the leading `v`). The in-app updater compares that assembly version to the latest GitHub release tag. If the assembly version is higher than the published tag, “Check for updates” will incorrectly report that the app is already up to date.

Example: for tag `v1.0.1`, set `<Version>1.0.1</Version>` before tagging.

## Release assets

Each release publishes:
- `ClipShot.exe` — the direct Windows executable
- `ClipShot.zip` — the portable distribution (browser download name: ClipShot.zip)
- `SHA256SUMS.txt` — SHA-256 hashes for the executable and ZIP

