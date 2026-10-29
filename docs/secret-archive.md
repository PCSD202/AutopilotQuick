# Encrypted secrets for AutopilotQuick

The administrator tool encrypts a folder into one `Secrets.aqsecrets` file. AutopilotQuick prompts for the passphrase before initializing telemetry, checking updates, creating its main window, or starting imaging. Incorrect passwords, damaged archives, and incomplete configuration stop startup. Credentials are decrypted in memory; only the encrypted archive is cached on the USB drive. Wi-Fi/BIOS provisioning packages and Dell tools are materialized under `X:\AQ\SecretRuntime` in WinPE and removed after use.

Encryption protects a copied archive when the copier does not know the passphrase. It does not replace nettools access control: restrict write access to administrators, limit read access to imaging networks/operators, disable directory browsing, and remove the old credential endpoints. Anyone with the shared passphrase gets every secret in the archive. A compromised imaging device can read unlocked secrets. An authenticated service issuing short-lived, scoped credentials is a stronger long-term design when the boot environment supports it.

## Build the tool

Use the .NET 10 SDK on an administrator workstation:

```powershell
dotnet publish .\AQ.SecretArchive.Tool\AQ.SecretArchive.Tool.csproj -c Release -r win-x64 --self-contained true -o .\artifacts\secret-archive-tool
```

The self-contained tool does not require installing .NET on the machine where you run it. The shared library targets .NET 6 for the existing app and .NET 10 for the tool. The app's .NET 6 runtime and older dependencies still need upgrading separately.

## Prepare a private staging folder

Keep staging outside Git, outside the web root, and outside synced/shared folders. Restrict its ACL to the administrator preparing the archive. Place these files in it, with the exact relative names below. Use newly rotated values.

| Relative filename | Required content |
| --- | --- |
| `GroupMan.json` | JSON strings `APIKEY`, `URL` |
| `TakehomeLoggerInfo.json` | JSON strings `ApiKey`, `Url` |
| `Takehome.json` | JSON strings `ApiKey`, `Url` |
| `AzureLogSettings.json` | JSON string `ConnectionString` for Azure Files |
| `AppInsights.json` | JSON string `ConnectionString` for Application Insights |
| `Bios/BiosPassword.txt` | Current BIOS password, UTF-8; trailing line endings are removed when used |
| `DellBiosSettings.zip` | Reviewed Dell bundle containing `cctk.exe`, `LaptopBiosSettings.cmd`, and `DesktopBiosSettings.cmd` at its root, plus dependencies |
| `Wifi.ppkg` | Reviewed current Wi-Fi provisioning package |
| `Invoke-BiosUpdate.ppkg` | Reviewed current BIOS update provisioning package |

All three service `Url`/`URL` fields must use HTTPS and contain no username/password in the URI. JSON text may use a UTF-8 BOM. Preserve the actual JSON schemas and existing Dell command behavior while replacing old secrets. The archive tool accepts other binary/text entries too; extra entries are encrypted but the app does not use them. Use a dedicated folder so unrelated secrets are not bundled accidentally.

The active cleanup step reads `Takehome.json` and calls its API URL with `ApiKey` in the `x-functions-key` header. The API performs Intune cleanup and, when take-home is enabled, Autopilot cleanup. The active pipeline does not use `TakehomeCreds.json`; omit it from the archive. Only the retained, inactive `IntuneCleanupStep` and `RemoveDeviceFromAutopilotStep` use direct Graph credentials. If those steps are explicitly re-enabled later, an optional `TakehomeCreds.json` must contain JSON strings `AppID`, `AppSecret`, `TenantID` and boolean `Encrypted: false`. It is validated when present. The old hard-coded password and field encryption helper have been removed; there is no legacy decryption fallback. Do not put any production credentials in example files or command arguments.

GitHub release checks and asset downloads use public access. Omit `GithubCreds.json`; the app no longer reads it, even if an existing archive contains it. No OAuth client ID, OAuth client secret, or personal access token is required. Repository `PCSD202/AutopilotQuick` and its releases must be public for this update path and the existing anonymous drive-creation flow to work. Until then, release checks can fail with a not-found response. Anonymous GitHub API requests share a limit of 60 requests per hour per public IP, so many devices behind the same network may exhaust it. Archive unlock and local imaging do not depend on a successful release check.

Changing repository visibility does not change nettools access. Before making it public again, review Git history and older release artifacts for the previously committed provisioning packages and hard-coded values; local deletion and `.gitignore` do not remove older copies. Rotated values must also be replaced in scripts/packages and legacy drive caches. This code change does not change GitHub repository visibility or the external creation PowerShell module.

Review the provisioning packages and scripts for embedded retired credentials, logging, and persisted files. Packages can intentionally provision settings into the installed OS; archive encryption does not hide those settings from administrators of that OS. Dell CCTK requires a password argument when clearing the BIOS password, so it can be observed by privileged local process inspection. Take-home mode invokes CCTK directly with separate native arguments to preserve the exact password, including quotes and non-ASCII characters. The app suppresses command output and never stores the password or a containing script in its normal cache or telemetry. BIOS failures report the numeric process exit code; launch failures report a Windows error code. Preserve that code when reporting a failed imaging run, without sharing passwords or full commands.

## BIOS settings in take-home mode

Take-home BIOS password removal passes a single blank space as the new setup password, as required by Dell's CLI reference; a zero-length argument can produce CCTK exit code 106 (invalid argument). The app preserves that space through native argument passing and still stops on a failed command. See [Dell's CCTK error codes](https://www.dell.com/support/kbdoc/en-us/000146881/dell-client-configuration-toolkit-cctk-error-codes) and the [setup password reference, page 85](https://www.manualshelf.com/manual/dell/command-configure-4-1/reference-guide-english/page-85.html).

## Create, inspect, extract, and change passwords

These commands run from the repository root and use example paths; select your own protected staging location:

```powershell
$archiveTool = '.\artifacts\secret-archive-tool\AQ.SecretArchive.Tool.exe'
& $archiveTool create 'C:\PrivateAQ\staging' 'C:\PrivateAQ\Secrets.aqsecrets'
& $archiveTool check 'C:\PrivateAQ\staging'
& $archiveTool list 'C:\PrivateAQ\Secrets.aqsecrets'
& $archiveTool extract 'C:\PrivateAQ\Secrets.aqsecrets' 'C:\PrivateAQ\new-extracted'
& $archiveTool rekey 'C:\PrivateAQ\Secrets.aqsecrets' 'C:\PrivateAQ\Secrets-next.aqsecrets'
& $archiveTool verify 'C:\PrivateAQ\Secrets.aqsecrets' # No password; checks length and any checksum
& $archiveTool checksum 'C:\PrivateAQ\Secrets-existing.aqsecrets' # Authenticate an older archive and add its checksum
```

Passphrases are entered interactively without echo. Creation and rekey require confirmation. No password flag, environment variable, or redirected input is supported. Escape cancels the prompt. Passwords of 6 to 1024 characters are accepted. A six-character password remains weak against offline guessing of a copied archive: SHA-256 hashing and deriving a 256-bit key do not increase its entropy. PBKDF2-HMAC-SHA256 with 600,000 iterations slows each guess; the possible passwords still determine the effective keyspace. A unique, randomly generated passphrase of at least 16 characters from a password manager is recommended. Store the passphrase in the team's approved password manager separately from the archive. There is no password recovery. Rebuild both the tool and app to use the six-character minimum; existing archives keep their current passwords and format.

`create` recursively includes regular files, rejects symbolic links/junctions and unsafe filenames, preserves source files, and requires output outside the source folder. `list` prints filenames only. `extract` requires a new directory and gives it an ACL restricted to the current user on Windows (mode 0700 elsewhere); it creates plaintext deliberately. Remove private staging/extraction folders when finished according to your storage policy; ordinary deletion does not guarantee secure erasure. Archive output must be a new file: creation/rekey never silently overwrite an existing archive or checksum. Writes use temporary files and atomic renames. `create` and `rekey` now also write `<output-archive>.sha256`; deploy that file together with its archive.

`checksum` prompts for the existing archive's password and authenticates it before adding a new `.sha256` file. It does not change the encrypted bytes or password, and refuses to overwrite an existing checksum. `verify` checks the public header, exact length, and the checksum when present without decrypting anything. Without a checksum, it explicitly reports that only structural checks passed; `list` still provides password-based authentication.

To edit secrets, extract to a private new folder, edit there, and create a new archive outside that folder. `rekey` decrypts/re-encrypts in memory. Changing the archive passphrase does not revoke downloaded old archives or rotate the secrets they contain. Rotate underlying credentials after exposure, and distribute a fresh archive and passphrase. Old offline clients must receive the new archive; each running app holds one snapshot until restart.

## Deploy and cut over

For a development imaging drive, run `AutopilotQuick\CopyDevBuildToDrive.ps1` from any working directory. It publishes a fresh development build, selects one volume labeled `OSDCloud` or `OSDCloudUSB`, copies application files, and verifies the executable hash. Supply `-DestinationPath 'F:\AutopilotQuick'` to choose a different folder or resolve multiple imaging volumes. Add `-SecretsArchivePath 'P:\PrivateAQ\Secrets.aqsecrets'` to copy the encrypted archive into the drive's `Cache\Secrets.aqsecrets` for automatic cached unlock. Its `.sha256` sidecar is verified and copied when present. An explicitly supplied legacy replacement removes any checksum belonging to the previous bundle. `-WhatIf` previews the operation without publishing or copying. Existing images, cache, logs, and device identity are preserved unless an archive replacement is explicitly supplied. No plaintext staging files are copied.

1. Prepare the complete archive with rotated values and test `list` locally. Publish the updated app onto a test imaging drive.
2. Serve `Secrets.aqsecrets` and its matching `Secrets.aqsecrets.sha256` under `https://nettools.psd202.org/AutoPilotFast/` over HTTPS. Ensure the server permits both extensions and sends their bytes unchanged. The checksum file contains only 64 SHA-256 hex characters and a newline, without a filename or BOM. Publish both from the same tool operation; a mismatched pair fails checking. The path is configuration, not a credential. Optionally set `AQ_SECRET_ARCHIVE_URL` to a different HTTPS URL on the boot environment; the checksum URL appends `.sha256` to that URL's path and preserves its query. Redirects and TLS certificate bypasses are rejected.
3. Startup automatically prepares Cache\Secrets.aqsecrets if present, with no network dependency. Without a cache, it waits for internet and retries network/temporary server failures every five seconds before enabling the password field. Cancel closes the window and cancels pending work. Select Download latest bundle to bypass an old cache and wait for a new download. Missing/forbidden URLs and redirects report their HTTP status; fix the server configuration and click Retry. Downloads use a timeout for stalled reads, allowing large bundles to take longer than 30 seconds in total.
4. Test cached/offline unlock, waiting for a new network connection, wrong password, cancel, checksum mismatch, and missing/invalid configuration on a test drive. Header, exact length, HTTP content length when supplied, and any checksum are checked before enabling the password field. A downloaded bundle is decrypted and validated before replacing the encrypted cache and checksum. A damaged cache or wrong password does not automatically fall back to another bundle; select Download latest bundle explicitly to replace it. Then test one controlled imaging run including Dell settings and provisioning packages. Do not consider a build or unit test an imaging validation.
5. Remove or deny all old credential URLs and plaintext web-root copies: `GroupMan.json`, `TakehomeCreds.json`, `TakehomeLoggerInfo.json`, `Takehome.json`, `AzureLogSettings.json`, `GithubCreds.json`, `Bios/BiosPassword.txt`, and any old sensitive Dell bundle. Audit other web-root folders, backups, directory listing, and related services for alternate copies. Restrict the server independently of the repo's visibility.
6. Remove old plaintext/field-encrypted caches from existing USB drives, including the corresponding `*-CacheData.json` sidecars, extracted `Cache\DellBiosSettings`, old provisioning packages under `Cache\TempDism`, and old application binaries. Do not delete unrelated OS images. Rebuild or retire older imaging drives that can still request the old paths.
7. Keep sensitive `Wifi.ppkg` and `Invoke-BiosUpdate.ppkg` source files in protected staging. Version 2 removes these files and the old encrypted credential XML from Git history and redacts historical hard-coded secret values, while retaining commits and development scripts. Historical release downloads are withdrawn. Replace existing clones with a fresh clone, and ask GitHub Support to remove retained pull-request references and cached sensitive views.

Published builds start the watchdog before the secret unlock window. The watchdog uses local logging and never unlocks secrets; only the worker prompts. Cancelling unlock stops the watchdog. A watchdog restart of the worker requires a new unlock. Use build and synthetic-data checks on development machines; validate the imaging UI only in an isolated test VM or a device approved for reimaging.

No nettools files, production credentials, server permissions, or deployed imaging drives are changed by building this code. Operators must complete the cutover; blocking old endpoints may break old clients, which is expected.

## Format and verification

### Compatible integrity checks

The encrypted `AQSECR01` format is unchanged, so older apps can still read archives made by the updated tool. Updated apps also accept older archives and servers where the checksum URL returns HTTP 404. Other checksum HTTP failures, malformed checksums, and mismatches stop loading instead of silently skipping verification. SHA-256 detects accidental corruption, including changes that preserve file length. It does not prove who published a bundle: someone able to replace both files can recompute it. AES-GCM authentication and configuration validation remain mandatory before startup and caching.

The app uses the cached sidecar offline without making network requests. For a legacy archive with no checksum, it can detect truncation or extra bytes from the existing header before prompting, but a wrong password and length-preserving damage still have indistinguishable AES-GCM failures. Successful authentication of a legacy cache saves a local checksum for future offline checks, when the drive is writable. Legacy downloads also receive a local checksum after successful unlock. A cached archive made with an older password can pass its checksum and still reject the current password; download the latest matching pair in that case. A server-supplied sidecar helps detect damage on the first download; a locally seeded checksum records the bytes previously accepted on that drive.

Cache file replacement uses separate atomic renames for the blob and sidecar. An interruption between them can leave a mismatch, which stops loading with an integrity error and is recovered by downloading the latest matching pair. Unverified downloads and failed password attempts do not replace the existing cache. Adding checksum files to nettools and updating deployed clients are separate deployment steps; building this code does not perform either.

Version 1 uses AES-256-GCM with a random 128-bit salt, random 96-bit nonce, and 128-bit authentication tag. PBKDF2-HMAC-SHA256 derives a 256-bit key with 600,000 iterations. The authenticated 44-byte header is ASCII `AQSECR01`, little-endian iteration count and ciphertext length, salt, then nonce. Ciphertext contains the entire ZIP (including filenames); the tag follows it. The KDF work factor is fixed for this version to bound processing of untrusted headers. Future KDF/format changes require a new version.

Limits are 272 MiB for the encrypted file, 256 MiB of total uncompressed content, and 1,024 entries. This accommodates the current Dell BIOS bundle (about 114 MiB compressed and 118 MiB extracted); the nested Dell ZIP has the same 256 MiB expansion limit. Both the administrator tool and the app must be rebuilt to use these limits. `check` validates source sizes and names using filesystem metadata only, without opening secret file contents or asking for a passphrase. Operational failures report their category or an explicitly authored message; secret values and raw exception details are never printed. The code rejects path traversal, Windows alternate data streams/device names, duplicate names ignoring case, and file/directory collisions. Nested Dell ZIP entries receive the same checks. Password and key buffers and owned plaintext byte arrays are cleared when possible; .NET strings returned to SDKs and operating-system process memory cannot be reliably erased.

```powershell
dotnet test .\AQ.SecretArchive.Tests\AQ.SecretArchive.Tests.csproj -f net10.0
dotnet test .\AQ.SecretArchive.Tests\AQ.SecretArchive.Tests.csproj -f net6.0
dotnet build .\AutopilotQuick\AutopilotQuick.csproj
```

CI installs both runtimes, tests the security boundary and app configuration, reads a .NET 10-generated archive with the .NET 6 library, builds the app, and publishes the administrator tool. Tests use synthetic data only. The new archive has not been tested against nettools or used for production imaging by these checks.
