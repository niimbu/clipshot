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
5. Run the workflow or push a new version tag. The workflow signs and verifies `ClipShot.exe` before creating the portable ZIP and release assets. If the secrets are not configured, the build remains unsigned.

Keep the PFX and password private. Never commit either one to the repository or include them in build artifacts.

## Chrome download warnings

A code signature is an important trust signal, but it cannot instantly create download reputation. New and low-volume applications can still receive warnings. If Chrome displays a warning, first confirm that the download came from the official [ClipShot Releases page](https://github.com/niimbu/clipshot/releases). Do not ask users to disable Safe Browsing or bypass warnings.

If the official release is incorrectly detected, submit the exact release URL and file to Google's [Safe Browsing site status / incorrect warning reporting](https://safebrowsing.google.com/safebrowsing/report_error/). Include the version and SHA-256 hash from `SHA256SUMS.txt`.

## Release assets

Each release publishes:
- `ClipShot.exe` — the direct Windows executable.
- `ClipShot-win-x64-portable.zip` — the portable distribution.
- `SHA256SUMS.txt` — SHA-256 hashes for the executable and ZIP.
