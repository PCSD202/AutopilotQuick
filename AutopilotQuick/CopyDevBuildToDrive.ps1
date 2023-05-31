dotnet restore "AutopilotQuick\AutopilotQuick.csproj"
dotnet publish "AutopilotQuick\AutopilotQuick.csproj" -p:PublishProfile=DevProfile -p:AssemblyVersion=6.6.7


$autopilotQuickPath = Get-PSDrive -PSProvider FileSystem | Where-Object {$_.Name -ne 'C'} | ForEach-Object {
    Get-Item "$($_.Name):\AutopilotQuick" -Force -ErrorAction Ignore
}

if($null -eq $autopilotQuickPath){
    $storageDriveLetter = (Get-Disk.storage | Where-Object {($_.FileSystemLabel -eq "OSDCloud" -or $_.FileSystemLabel -eq "OSDCloudUSB")}).DriveLetter
    $autopilotQuickPath = $storageDriveLetter+":\AutopilotQuick"
    New-Item -Path $autopilotQuickPath -ItemType "directory"
} else {
    $autopilotQuickPath = Join-Path $autopilotQuickPath.Root.Name "AutopilotQuick"
}



Robocopy.exe "C:\Users\AMoore\source\repos\PCSD202\AutopilotQuick\AutopilotQuick\bin\Release\net6.0-windows\publish\" $autopilotQuickPath