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
    
    private int CurrentStep = 0;
    private int MaxSteps = 7;
    private void IncProgress()
    {
        CurrentStep++;
        Progress = ((double)CurrentStep / MaxSteps) * 100;
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
        
        
        WaitWhilePaused(pauseToken);
        Message = "Looking up service tag...";
        IncProgress();
        WMIHelper helper = new WMIHelper("root\\CimV2");
        var serviceTag = helper.QueryFirstOrDefault<Bios>().SerialNumber;
        
        WaitWhilePaused(pauseToken);
        Message = "Loading credentials...";
        IncProgress();
        //Gets the decrypted credentials from the encrypted file
        var GraphCreds = await GraphHelper.GetGraphCreds(context);
        Logger.Info($"Credentials Loaded");
        
        WaitWhilePaused(pauseToken);
        Message = "Connecting to Microsoft Graph...";
        IncProgress();
        var graphClient = GraphHelper.ConnectToMSGraph(GraphCreds);
        Logger.Info($"Connected to MSGraph");
        
        WaitWhilePaused(pauseToken);
        Message = $"Looking up ({serviceTag})'s autopilot record...";
        IncProgress();
        Logger.Info($"Looking up autopilot record for device");
        var autopilotRecord = await GraphHelper.GetWindowsAutopilotDevice(serviceTag, graphClient, Logger);

        if (autopilotRecord is null)
        {
            Progress = 100;
            Message = "No autopilot record found for device";
            return new StepResult(true, "No autopilot record found for device");
        }
        Logger.Info($"Found autopilot record for device: {JsonConvert.SerializeObject(autopilotRecord)}");
        
        WaitWhilePaused(pauseToken);
        Message = $"Looking up ({serviceTag})'s intune object...";
        IncProgress();
        Logger.Info($"Looking up intune object with id: {autopilotRecord.ManagedDeviceId}");
        var intuneObject = await GraphHelper.GetIntuneObject(autopilotRecord.ManagedDeviceId, graphClient, Logger);

        if (intuneObject is null)
        {
            Progress = 100;
            Message = "No intune object found for device";
            return new StepResult(true, "No intune object found for device");
        }
        Logger.Info($"Found intune object for device: {JsonConvert.SerializeObject(intuneObject)}");

        WaitWhilePaused(pauseToken);
        Message = "Deleting intune object...";
        IncProgress();
        try
        {
            Logger.Info($"Deleting intune object...");
            await graphClient.DeviceManagement.ManagedDevices[autopilotRecord.ManagedDeviceId].Request().DeleteAsync();
        }
        catch (ServiceException e)
        {
            Logger.Info($"Got error trying to delete intune object id: {autopilotRecord.ManagedDeviceId}");
        }

        IncProgress();
        Message = "Deleted intune object successfully";
        return new StepResult(true, "Deleted intune object for device");
    }
}