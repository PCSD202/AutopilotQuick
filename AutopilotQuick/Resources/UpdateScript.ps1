Write-Host "Finalizing update"
Write-Host "Waiting for AutopilotQuick to exit"

while ( (Get-Process -Name "AutopilotQuick" -ErrorAction SilentlyContinue).Count -ne 0 )
{
    Write-Host '.' -NoNewline
    Start-Sleep -Milliseconds 400
}

if(!(Test-Path "$AutopilotQuickPath\Update\AutopilotQuick.exe")){
    Write-Host "Could not complete update because update folder has no AutopilotQuick.exe"
    X:\Scripts\Launcher.ps1
    break
}

$ImportantFiles = @(
    "DeviceIdentifier.json"
)

$ImportantFolders = @(
    "Cache",
    "logs", "Update",
    "Modules"
)

$XFArg = "/XF"
foreach ($i in $ImportantFiles){
    $XFArg += " '$AutopilotQuickPath\$i'"
}

$XDArg = "/XD"
foreach ($i in $ImportantFolders){
    $XDArg += " '$AutopilotQuickPath\$i'"
}


"robocopy $AutopilotQuickPath\Update $AutopilotQuickPath /MOVE /MIR $XFArg $XDArg" | Invoke-Expression

X:\Scripts\Launcher.ps1