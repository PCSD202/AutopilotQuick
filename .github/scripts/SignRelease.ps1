param(
    [Parameter(Mandatory)][string] $ArchivePath,
    [string] $PublicKeyPath = 'AutopilotQuick/Resources/AutopilotQuick_PubKey.asc'
)
$ErrorActionPreference = 'Stop'
$gpg = Join-Path $env:ProgramFiles 'Git/usr/bin/gpg.exe'
if (!(Test-Path -LiteralPath $gpg)) { throw 'Git for Windows GPG is required.' }
if (!$env:AQ_SIGNING_KEY) { throw 'The release signing key is missing.' }
$root = Join-Path ([IO.Path]::GetTempPath()) ('aq-sign-' + [guid]::NewGuid().ToString('N'))
$privateHome = Join-Path $root 'private'
$publicHome = Join-Path $root 'public'
$hashFile = Join-Path $root 'sha256.txt'
$archive = (Resolve-Path -LiteralPath $ArchivePath).Path
$signature = $archive + '.sha256.pgp'

function ConvertTo-GpgPath([string] $Path) {
    $full = [IO.Path]::GetFullPath($Path).Replace('\', '/')
    if ($full -notmatch '^([A-Za-z]):/(.*)$') { throw 'GPG requires a local drive path.' }
    '/' + $Matches[1].ToLowerInvariant() + '/' + $Matches[2]
}

function Invoke-Gpg([string[]] $Arguments, [string] $InputText = '') {
    $start = [Diagnostics.ProcessStartInfo]::new($gpg)
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardInput = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    foreach ($argument in $Arguments) { $start.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::Start($start)
    try {
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        $process.StandardInput.Write($InputText)
        $process.StandardInput.Close()
        $process.WaitForExit()
        $null = $stdout.GetAwaiter().GetResult()
        $null = $stderr.GetAwaiter().GetResult()
        if ($process.ExitCode -ne 0) { throw 'Release signing or signature verification failed.' }
    }
    finally { $process.Dispose() }
}

try {
    $null = New-Item -ItemType Directory -Path $privateHome, $publicHome
    [IO.File]::WriteAllText($hashFile, (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash + "`n", [Text.UTF8Encoding]::new($false))
    Invoke-Gpg @('--homedir', (ConvertTo-GpgPath $privateHome), '--batch', '--import') $env:AQ_SIGNING_KEY
    Invoke-Gpg @('--homedir', (ConvertTo-GpgPath $privateHome), '--batch', '--yes', '--pinentry-mode', 'loopback', '--passphrase-fd', '0', '--armor', '--output', (ConvertTo-GpgPath $signature), '--clearsign', (ConvertTo-GpgPath $hashFile)) ($env:AQ_SIGNING_PASSPHRASE + "`n")
    Invoke-Gpg @('--homedir', (ConvertTo-GpgPath $publicHome), '--batch', '--import') ([IO.File]::ReadAllText((Resolve-Path -LiteralPath $PublicKeyPath).Path))
    Invoke-Gpg @('--homedir', (ConvertTo-GpgPath $publicHome), '--batch', '--verify', (ConvertTo-GpgPath $signature))
    Write-Host 'Release hash signed and verified against the app public key.'
}
finally {
    $env:AQ_SIGNING_KEY = $null
    $env:AQ_SIGNING_PASSPHRASE = $null
    if ((Test-Path -LiteralPath $root) -and ([IO.Path]::GetFullPath($root).StartsWith([IO.Path]::GetTempPath(), [StringComparison]::OrdinalIgnoreCase))) {
        Remove-Item -LiteralPath $root -Recurse -Force
    }
}
