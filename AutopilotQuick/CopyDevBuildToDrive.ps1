#requires -Version 5.1
<#
.SYNOPSIS
Publishes a fresh development build and copies it to an AutopilotQuick imaging drive.
.EXAMPLE
.\AutopilotQuick\CopyDevBuildToDrive.ps1
.EXAMPLE
.\AutopilotQuick\CopyDevBuildToDrive.ps1 -DestinationPath 'F:\AutopilotQuick' -SecretsArchivePath 'P:\PrivateAQ\Secrets.aqsecrets'
.EXAMPLE
.\AutopilotQuick\CopyDevBuildToDrive.ps1 -DestinationPath 'F:\AutopilotQuick' -WhatIf
.NOTES
Automatic selection requires one volume labeled OSDCloud or OSDCloudUSB.
Existing Cache, logs, Update, and device identity files are preserved.
Only an explicitly supplied encrypted archive is copied; no plaintext staging files are copied.
An accompanying .sha256 file is verified and copied when present; legacy archives are supported.
#>
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string] $DestinationPath,
    [string] $SecretsArchivePath,
    [ValidatePattern('^\d+\.\d+\.\d+(\.\d+)?$')]
    [string] $AssemblyVersion = '6.6.7'
)

$ErrorActionPreference = 'Stop'
$projectPath = Join-Path $PSScriptRoot 'AutopilotQuick.csproj'
$repositoryPath = Split-Path -Parent $PSScriptRoot

if ([string]::IsNullOrWhiteSpace($DestinationPath)) {
    $volumes = @(Get-Volume | Where-Object {
        $_.DriveLetter -and $_.FileSystemLabel -in @('OSDCloud', 'OSDCloudUSB')
    })
    if ($volumes.Count -eq 0) {
        throw 'No OSDCloud or OSDCloudUSB volume found. Supply -DestinationPath, for example F:\AutopilotQuick.'
    }
    if ($volumes.Count -gt 1) {
        $driveNames = ($volumes | ForEach-Object { '{0}: ({1})' -f $_.DriveLetter, $_.FileSystemLabel }) -join ', '
        throw "Multiple imaging volumes found: $driveNames. Select one with -DestinationPath."
    }
    $DestinationPath = '{0}:\AutopilotQuick' -f $volumes[0].DriveLetter
}

$destination = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($DestinationPath)
$destination = [IO.Path]::GetFullPath($destination).TrimEnd('\', '/')
if ([string]::Equals($destination, [IO.Path]::GetPathRoot($destination).TrimEnd('\', '/'), [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Destination must be an application folder, not the root of a drive.'
}
if (Test-Path -LiteralPath $destination -PathType Leaf) {
    throw 'Destination is a file. Supply an application folder.'
}
if (-not (Test-Path -LiteralPath ([IO.Path]::GetPathRoot($destination)) -PathType Container)) {
    throw 'The destination drive or share is unavailable.'
}
if ([string]::Equals($destination, $repositoryPath, [StringComparison]::OrdinalIgnoreCase) -or
    [string]::Equals($destination, $PSScriptRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Destination cannot be the repository or project folder.'
}

$archive = $null
$archiveChecksum = $null
$archiveHash = $null
if (-not [string]::IsNullOrWhiteSpace($SecretsArchivePath)) {
    $archive = (Resolve-Path -LiteralPath $SecretsArchivePath).ProviderPath
    if (-not (Test-Path -LiteralPath $archive -PathType Leaf)) { throw 'Secrets archive must be a file.' }
    if ([IO.Path]::GetExtension($archive) -ine '.aqsecrets') { throw 'Secrets archive must have the .aqsecrets extension.' }
    # Check the public header and exact length; never decrypt secrets during deployment.
    $archiveStream = [IO.File]::OpenRead($archive)
    try {
        $header = New-Object byte[] 16
        if ($archiveStream.Length -lt 60 -or $archiveStream.Length -gt (272 * 1024 * 1024) -or
            $archiveStream.Read($header, 0, 16) -ne 16 -or
            [Text.Encoding]::ASCII.GetString($header, 0, 8) -ne 'AQSECR01' -or
            [BitConverter]::ToInt32($header, 8) -ne 600000 -or
            [BitConverter]::ToInt32($header, 12) -ne ($archiveStream.Length - 60)) {
            throw 'The supplied AQ secret archive is incomplete or has an invalid header.'
        }
    }
    finally { $archiveStream.Dispose() }
    $archiveHash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash
    $checksumPath = $archive + '.sha256'
    if (Test-Path -LiteralPath $checksumPath) {
        if (-not (Test-Path -LiteralPath $checksumPath -PathType Leaf) -or
            (Get-Item -LiteralPath $checksumPath).Length -gt 128) {
            throw 'The supplied archive checksum file is invalid.'
        }
        $archiveChecksum = [IO.File]::ReadAllText($checksumPath, [Text.Encoding]::ASCII).Trim()
        if ($archiveChecksum -notmatch '\A[0-9a-fA-F]{64}\z' -or $archiveChecksum -ine $archiveHash) {
            throw 'The supplied encrypted archive does not match its .sha256 checksum. Use a matching pair.'
        }
    }
}

Get-Command dotnet -ErrorAction Stop | Out-Null
Get-Command Robocopy.exe -ErrorAction Stop | Out-Null
$publishPath = Join-Path $repositoryPath ('artifacts\dev-drive-publish\' + [Guid]::NewGuid().ToString('N'))
$action = 'Publish development build and copy application files'
if ($archive) { $action += ' plus Cache\Secrets.aqsecrets' }
if (-not $PSCmdlet.ShouldProcess($destination, $action)) { return }

Write-Host "Publishing development build $AssemblyVersion..."
# publish includes restore. A new output folder prevents stale files from being deployed.
& dotnet publish $projectPath '-p:PublishProfile=DevProfile' '-p:Configuration=Debug' "-p:AssemblyVersion=$AssemblyVersion" --output $publishPath --nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE. No files were copied to the drive." }
$publishedExecutable = Join-Path $publishPath 'AutopilotQuick.exe'
if (-not (Test-Path -LiteralPath $publishedExecutable -PathType Leaf)) {
    throw "Publish output is missing AutopilotQuick.exe: $publishPath. No files were copied to the drive."
}

New-Item -Path $destination -ItemType Directory -Force | Out-Null
# Never mirror or purge: images, encrypted cache, logs, updates, and identity belong to the drive.
& Robocopy.exe $publishPath $destination /E /XJ /R:2 /W:1 /NP /NFL /NDL /NJH /NJS /XD Cache logs Update /XF DeviceIdentifier.json
$copyExitCode = $LASTEXITCODE
if ($copyExitCode -ge 8) { throw "Application copy failed (Robocopy exit code $copyExitCode). The drive may contain a partial copy." }
$deployedExecutable = Join-Path $destination 'AutopilotQuick.exe'
if ((Get-FileHash -LiteralPath $publishedExecutable -Algorithm SHA256).Hash -ne
    (Get-FileHash -LiteralPath $deployedExecutable -Algorithm SHA256).Hash) {
    throw 'Executable verification failed after copying.'
}

if ($archive) {
    $cacheDirectory = Join-Path $destination 'Cache'
    New-Item -Path $cacheDirectory -ItemType Directory -Force | Out-Null
    $archiveDestination = Join-Path $cacheDirectory 'Secrets.aqsecrets'
    if (-not [string]::Equals($archive, $archiveDestination, [StringComparison]::OrdinalIgnoreCase)) {
        $temporaryArchive = Join-Path $cacheDirectory ('Secrets.' + [Guid]::NewGuid().ToString('N') + '.tmp')
        try {
            Copy-Item -LiteralPath $archive -Destination $temporaryArchive
            if ($archiveHash -ne (Get-FileHash -LiteralPath $temporaryArchive -Algorithm SHA256).Hash) {
                throw 'Encrypted archive verification failed; the previous archive was preserved.'
            }
            Move-Item -LiteralPath $temporaryArchive -Destination $archiveDestination -Force
        }
        finally { if (Test-Path -LiteralPath $temporaryArchive) { Remove-Item -LiteralPath $temporaryArchive } }
    }
    if ($archiveHash -ne (Get-FileHash -LiteralPath $archiveDestination -Algorithm SHA256).Hash) {
        throw 'The encrypted archive changed during deployment. Retry with a matching archive and checksum.'
    }
    $checksumDestination = $archiveDestination + '.sha256'
    if ($archiveChecksum) {
        $temporaryChecksum = $checksumDestination + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'
        try {
            [IO.File]::WriteAllText($temporaryChecksum, $archiveChecksum + "`n", [Text.Encoding]::ASCII)
            Move-Item -LiteralPath $temporaryChecksum -Destination $checksumDestination -Force
        }
        finally { if (Test-Path -LiteralPath $temporaryChecksum) { Remove-Item -LiteralPath $temporaryChecksum } }
        Write-Host 'Matching SHA-256 checksum copied for offline integrity checking.'
    }
    elseif (Test-Path -LiteralPath $checksumDestination) {
        # An explicitly supplied legacy replacement must not inherit a checksum belonging to the old bundle.
        Remove-Item -LiteralPath $checksumDestination
    }
    Write-Host 'Encrypted archive copied to Cache\Secrets.aqsecrets (used automatically at startup).'
}
else {
    Write-Host 'No archive supplied. Startup uses the existing encrypted cache, or waits for internet to download the bundle.'
}
Write-Host "Development build copied and executable verified: $destination"
Write-Host "Published files: $publishPath"
$global:LASTEXITCODE = 0
