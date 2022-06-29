function Install-ModuleIfNotFound {
    [cmdletbinding()]
    param
    (
        [Parameter(Mandatory=$true)]
        [string] $ModuleName
    )
    $module = Import-Module $ModuleName -PassThru -ErrorAction Ignore
    if (-not $module) {
        Write-Host "Installing module $($ModuleName)..."
        Install-Module -Name $ModuleName
    }
}
#Lets setup our dependancies
Install-PackageProvider -Name NuGet -MinimumVersion 2.8.5.201 -Force 6>$null 1>$null
Install-Module Microsoft.Graph.Authentication -Repository PSGallery -Force
Install-Module Microsoft.Graph.DeviceManagement -Repository PSGallery -Force
Install-Module Microsoft.Graph.DeviceManagement.Enrolment -Repository PSGallery -Force

function Cleanup-Autopilot {
    param
    (
        [Parameter()]
        $appid,
        
        [Parameter()]
        $tenantid,
        
        [Parameter()]
        $clientsecret
    )
    Import-Module Microsoft.Graph.Authentication
    Import-Module Microsoft.Graph.DeviceManagement
    Import-Module Microsoft.Graph.DeviceManagement.Enrolment
    $body =  @{
        Grant_Type    = "client_credentials"
        Scope         = "https://graph.microsoft.com/.default"
        Client_Id     = $appid
        Client_Secret = $clientsecret
    }
     
    $connection = Invoke-RestMethod -Uri "https://login.microsoftonline.com/$tenantid/oauth2/v2.0/token" -Method POST -Body $body
     
    $token = $connection.access_token
    
    $graphid = Connect-MgGraph -AccessToken $token
    
    $serial = (Get-WmiObject -Class Win32_bios).SerialNumber
    #Check to see if there is an autopilot device
    if ($serial.Length -ge 6 -and $graphid){
    
        $autopilotdevice = Get-MgDeviceManagementWindowAutopilotDeviceIdentity -Filter "contains(serialNumber,'$serial')"
        if ($autopilotdevice){
            try {
                $intunedevice = Get-MgDeviceManagementManagedDevice -ManagedDeviceId $autopilotdevice.ManagedDeviceId -ErrorAction Stop
            }
            catch {
                $intunedevice = $null
            }
            if ($intunedevice) {
                Remove-MgDeviceManagementManagedDevice -ManagedDeviceId $autopilotdevice.ManagedDeviceId 
            }
            Remove-MgDeviceManagementWindowAutopilotDeviceIdentity -WindowsAutopilotDeviceIdentityId $autopilotdevice.id 
        }   
        
    }
}

