using System.Linq;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using AutopilotQuick.WMI;
using Azure.Identity;
using Microsoft.Graph;
using Newtonsoft.Json;
using Nito.AsyncEx;
using NLog;
using ORMi;
using File = System.IO.File;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace AutopilotQuick.Steps;

public class IntuneCleanupStep : StepBaseEx
{
    public readonly Logger Logger = LogManager.GetCurrentClassLogger();

    
    async Task<WindowsAutopilotDeviceIdentity?> GetWindowsAutopilotDevice(string Serial, GraphServiceClient client)
    {
        try
        {
            var devices = await client.DeviceManagement.WindowsAutopilotDeviceIdentities.Request()
                .Filter($"contains(serialNumber,'{Serial}')").GetAsync();
            return devices.Count >= 1 ? devices.First() : null;
        }
        catch (ServiceException e)
        {
            if (e.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }
            Logger.Error($"Got error while trying to look up autopilot record with st {Serial}");
            Logger.Error(e);
            return null;
        }
        
    }
    
    async Task<ManagedDevice?> GetIntuneObject(string ManagedDeviceID, GraphServiceClient client)
    {
        try
        {
            var device = await client.DeviceManagement.ManagedDevices[ManagedDeviceID].Request().GetAsync();
            return device;
        }
        catch (ServiceException e)
        {
            if (e.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }
            Logger.Error($"Got error while trying to look up intune object with id: {ManagedDeviceID}");
            Logger.Error(e);
            return null;
        }
        
    }
    
    public override async Task<StepResult> Run(UserDataContext context, PauseToken pauseToken)
    {
        if (!IsEnabled)
        {
            Title = "Cleaning up intune records - DISABLED";
            await Task.Run(() => CountDown(pauseToken, 5000));
            return new StepResult(true, "Cleaning up autopilot records - DISABLED");
        }

        if (!InternetMan.getInstance().IsConnected)
        {
            Title = "Cleaning up intune records - NO INTERNET";
            Progress = 100;
            await Task.Run(() => CountDown(pauseToken, 5000));
            return new StepResult(true, "Skipped cleaning up autopilot records due to not having internet");
        }
        Title = "Cleaning up intune records";
        Progress = 0;
        
        IsIndeterminate = false;
        Message = "Looking up service tag...";
        WMIHelper helper = new WMIHelper("root\\CimV2");
        var serviceTag = helper.QueryFirstOrDefault<Bios>().SerialNumber;
        
        
        Message = "Loading credentials...";
        //Gets the decrypted credentials from the encrypted file
        var GraphCreds = await GraphHelper.GetGraphCreds(context);
        Logger.Info($"Credentials Loaded");
        
        Message = "Connecting to Microsoft Graph...";
        var graphClient = GraphHelper.ConnectToMSGraph(GraphCreds);
        Logger.Info($"Connected to MSGraph");
        
        Message = $"Looking up ({serviceTag})'s autopilot record...";
        Logger.Info($"Looking up autopilot record for device");
        var autopilotRecord = await GetWindowsAutopilotDevice(serviceTag, graphClient);

        if (autopilotRecord is null)
        {
            Progress = 100;
            Message = "No autopilot record found for device";
            return new StepResult(true, "No autopilot record found for device");
        }
        Logger.Info($"Found autopilot record for device: {JsonConvert.SerializeObject(autopilotRecord)}");
        
        Message = $"Looking up ({serviceTag})'s intune object...";
        Logger.Info($"Looking up intune object with id: {autopilotRecord.ManagedDeviceId}");
        var intuneObject = await GetIntuneObject(autopilotRecord.ManagedDeviceId, graphClient);

        if (intuneObject is null)
        {
            Progress = 100;
            Message = "No intune object found for device";
            return new StepResult(true, "No intune object found for device");
        }
        Logger.Info($"Found intune object for device: {JsonConvert.SerializeObject(intuneObject)}");

        
        try
        {
            Logger.Info($"Deleting intune object...");
            await graphClient.DeviceManagement.ManagedDevices[autopilotRecord.ManagedDeviceId].Request().DeleteAsync();
        }
        catch (ServiceException e)
        {
            Logger.Info($"Got error trying to delete intune object id: {autopilotRecord.ManagedDeviceId}");
        }

        Progress = 100;
        Message = "Deleted intune object successfully";
        return new StepResult(true, "Deleted intune object for device");
    }
}