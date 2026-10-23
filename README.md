# Autopilot Quick
Autopilot Quick is a tool we use to image laptops (and other devices) in PSD202. It formats the largest drive, applies windows, and sets the bios settings.

## How to use?
This application is very specific to PSD202. The source is public; deployment credentials and provisioning packages are kept in a separate encrypted archive.

Credentials and provisioning packages are loaded from a password-encrypted archive at startup. See [the migration and archive tool guide](docs/secret-archive.md) before deploying this version.

**Requirements**:
- Be connected to the district intranet
- An AutopilotQuick drive

All you need to do after the requirements are met is to boot off of the drive.

## Creating an AutopilotQuick drive
You can start the installer by running the following code in an administrator powershell window
```powershell
Install-Module AutopilotQuick
Set-ExecutionPolicy Bypass
New-AutopilotQuickDrive
```
