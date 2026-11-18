# Releasing ClipShot for Windows

## Why Windows blocks ClipShot today

CI logs for current releases show:

```text
Signing secrets are not configured; continuing with an unsigned build.
```

Unsigned executables trigger **Microsoft Defender SmartScreen** (“Windows protected your PC”). Users can click **More info → Run anyway**, but the lasting fix is Authenticode signing.

## Authenticode signing

The Windows build workflow already signs when these GitHub Actions secrets exist:

| Secret | Value |
| --- | --- |
| `WINDOWS_SIGNING_CERTIFICATE_BASE64` | Base64 of the password-protected `.pfx` (no line breaks required) |
| `WINDOWS_SIGNING_CERTIFICATE_PASSWORD` | PFX password |

A **self-signed** certificate is not enough for SmartScreen. Use a publicly trusted code-signing certificate (or a cloud signing service that issues one).

### Practical options for open-source

1. **[SignPath.io](https://signpath.io/)** — free code signing for open-source projects; integrates with GitHub Actions.
2. **[Azure Trusted Signing](https://learn.microsoft.com/en-us/azure/trusted-signing/)** — Microsoft’s cloud signing service (paid; good SmartScreen reputation path).
3. A standard OV/EV code-signing certificate from a public CA, exported as PFX.

### Configure the existing workflow (PFX)

1. Obtain a trusted code-signing certificate and export it as a password-protected PFX (private key included).
2. Base64-encode the PFX without changing its contents. PowerShell:

   ```powershell
   [Convert]::ToBase64String([IO.File]::ReadAllBytes(".\publisher.pfx")) | Set-Clipboard
   ```

3. In the GitHub repo: **Settings → Secrets and variables → Actions**, add:
   - `WINDOWS_SIGNING_CERTIFICATE_BASE64`
   - `WINDOWS_SIGNING_CERTIFICATE_PASSWORD`
4. Push a new version tag. The workflow signs and verifies `ClipShot.exe` before packaging. If the secrets are missing, the build stays unsigned.

Never commit the PFX or password. Never paste them into chat or issues.

After signing is enabled, ship a new release so every download is signed. SmartScreen reputation still builds with download volume over time; EV/Trusted Signing usually clears faster than unsigned binaries.

## Chrome download warnings

Prefer the PowerShell installer in [DISTRIBUTION.md](DISTRIBUTION.md) so users avoid browser ZIP downloads.

If Chrome warns on an official release, confirm the URL is from [ClipShot Releases](https://github.com/niimbu/clipshot/releases). Do not ask users to disable Safe Browsing. Report false positives to Google [Safe Browsing](https://safebrowsing.google.com/safebrowsing/report_error/) with the SHA-256 from `SHA256SUMS.txt`.

## Versioning

Keep `<Version>` in `src/ClipShot.App/ClipShot.App.csproj` identical to the Git tag (without the leading `v`). The in-app updater compares the assembly version to the latest GitHub release tag.

Example: for tag `v1.0.12`, set `<Version>1.0.12</Version>` before tagging.

## Release assets

Each release publishes:

- `ClipShot.exe` — the direct Windows executable
- `ClipShot.zip` — the portable distribution
- `SHA256SUMS.txt` — SHA-256 hashes for the executable and ZIP
