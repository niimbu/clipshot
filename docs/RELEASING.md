# Releasing ClipShot for Windows

## Why Windows blocks ClipShot today

If CI logs show unsigned builds, SmartScreen shows **Windows protected your PC**. Users can click **More info → Run anyway**. The lasting fix is Authenticode signing via **SignPath OSS** (free for open source).

## SignPath OSS (recommended — free)

### 1. Apply

1. Open https://signpath.io/solutions/open-source-community
2. Apply for free open-source signing for `https://github.com/niimbu/clipshot`
3. Wait for approval (often a few days)

### 2. Configure the SignPath project

After approval, in the SignPath dashboard:

| Setting | Suggested value |
| --- | --- |
| Project slug | `clipshot` |
| Signing policy slug | `release-signing` (auto-approve for CI is fine for OSS) |
| Trusted build system | GitHub.com |
| Repository | `niimbu/clipshot` |
| Artifact configuration | paste [`.signpath/artifact-configurations/default.xml`](../.signpath/artifact-configurations/default.xml) as the **default** |

Also install the **SignPath GitHub App** on the `niimbu/clipshot` repository.

### 3. Add GitHub Actions secret + variables

In the repo: **Settings → Secrets and variables → Actions**

| Kind | Name | Value |
| --- | --- | --- |
| Secret | `SIGNPATH_API_TOKEN` | API token from SignPath (submitter permission) |
| Variable | `SIGNPATH_ORGANIZATION_ID` | Organization UUID from SignPath |
| Variable | `SIGNPATH_PROJECT_SLUG` | `clipshot` |
| Variable | `SIGNPATH_SIGNING_POLICY_SLUG` | `release-signing` |

Do **not** paste the API token into chat or commit it.

### 4. Ship a signed release

Bump `<Version>` in `src/ClipShot.App/ClipShot.App.csproj`, then:

```powershell
git tag v1.0.13
git push origin v1.0.13
```

On `v*` tags, the workflow uploads `ClipShot.exe` to SignPath, waits for signing, then publishes the signed exe/zip. CI will note when SignPath is enabled.

Until secrets/vars are set, tag builds stay unsigned (with a warning) so releases do not hard-fail.

## PFX fallback (optional)

If you later buy your own certificate, you can still use:

- `WINDOWS_SIGNING_CERTIFICATE_BASE64`
- `WINDOWS_SIGNING_CERTIFICATE_PASSWORD`

PFX signing runs only when SignPath is not configured.

## Chrome download warnings

Prefer the PowerShell installer in [DISTRIBUTION.md](DISTRIBUTION.md). Report false positives to Google [Safe Browsing](https://safebrowsing.google.com/safebrowsing/report_error/) with the SHA-256 from `SHA256SUMS.txt`.

## Versioning

Keep `<Version>` in `src/ClipShot.App/ClipShot.App.csproj` identical to the Git tag (without the leading `v`).

## Release assets

- `ClipShot.exe` — Windows executable
- `ClipShot.zip` — portable package
- `SHA256SUMS.txt` — SHA-256 hashes
