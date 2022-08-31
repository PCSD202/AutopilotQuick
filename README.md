# Autopilot Quick
Autopilot Quick is a tool we use to image laptops (and other devices) in PSD202. It formats the largest drive, applies windows, and sets the bios settings.

## How to use?
This application is very specific to PSD202, this code is open source to basically show off how to do imaging.

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
