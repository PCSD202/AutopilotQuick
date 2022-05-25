$PowerStatus = (Get-WmiObject -Class BatteryStatus  -Namespace root\wmi -ErrorAction SilentlyContinue).PowerOnLine
if ($PowerStatus){
    # Suspend Bitlocker for 1 reboot cycle
    Suspend-Bitlocker -MountPoint "C:" -RebootCount 1
    start-process -FilePath ".\flash64w.exe" -ArgumentList "/b=Latitude_3180_3189_1.10.0.exe /s /f /forceit /p=PCSD202" -Wait
    exit 1641
}
else {
    write-host "Device is not plugged in"
    exit 1618
}
