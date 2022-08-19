using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using AutopilotQuick.WMI;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Newtonsoft.Json;
using Nito.AsyncEx;
using ORMi;

namespace AutopilotQuick.Steps;

public class RemoveDeviceFromAutopilotStep : StepBaseEx
{
    public override string Name() => "Remove device from autopilot step";
    public readonly ILogger Logger = App.GetLogger<RemoveUnattendXMLStep>();
    
    private int CurrentStep = 0;
    private int MaxSteps = 7;
    private void IncProgress()
    {
        CurrentStep++;
        Progress = ((double)CurrentStep / MaxSteps) * 100;
    }
    public override async Task<StepResult> Run(UserDataContext context, PauseToken pauseToken,
        IOperationHolder<RequestTelemetry> StepOperation)
    {
        if (!IsEnabled)
        {
            Title = "Removing device from ap - DISABLED";
            await Task.Run(() => CountDown(pauseToken, 5000));
            return new StepResult(true, "Removing device from ap - DISABLED");
        }

        if (!context.TakeHomeToggleOn)
        {
            Progress = 100;
            Title = "Removing device from ap - DISABLED";
            return new StepResult(true, "Finished removing device from autopilot");
        }
        Title = "Removing device from autopilot";
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
        Logger.LogInformation($"Credentials Loaded");
        
        WaitWhilePaused(pauseToken);
        Message = "Connecting to Microsoft Graph...";
        IncProgress();
        var graphClient = GraphHelper.ConnectToMSGraph(GraphCreds);
        Logger.LogInformation($"Connected to MSGraph");
        
        WaitWhilePaused(pauseToken);
        Message = $"Looking up ({serviceTag})'s autopilot record...";
        IncProgress();
        Logger.LogInformation($"Looking up autopilot record for device");
        var autopilotRecord = await GraphHelper.GetWindowsAutopilotDevice(serviceTag, graphClient, Logger);

        if (autopilotRecord is null)
        {
            Progress = 100;
            Message = "No autopilot record found for device";
            return new StepResult(true, "No autopilot record found for device");
        }
        Logger.LogInformation("Found autopilot record for device: {autopilotRecord}", autopilotRecord);
        
        WaitWhilePaused(pauseToken);
        Message = $"Looking up ({serviceTag})'s intune object...";
        IncProgress();
        Logger.LogInformation("Looking up intune object with id: {id}", autopilotRecord.ManagedDeviceId);
        var intuneObject = await GraphHelper.GetIntuneObject(autopilotRecord.ManagedDeviceId, graphClient, Logger);

        if (intuneObject is null)
        {
            Progress = 100;
            Message = "No intune object found for device";
            return new StepResult(true, "No intune object found for device");
        }
        Logger.LogInformation($"Found intune object for device: {intuneObject}", intuneObject);

        WaitWhilePaused(pauseToken);
        Message = "Deleting intune object...";
        IncProgress();
        try
        {
            Logger.LogInformation($"Deleting intune object...");
            await graphClient.DeviceManagement.ManagedDevices[autopilotRecord.ManagedDeviceId].Request().DeleteAsync();
        }
        catch (ServiceException e)
        {
            Logger.LogError(e, "Got error trying to delete intune object id: {id}", autopilotRecord.ManagedDeviceId);
        }
        
        Logger.LogInformation("Deleting Autopilot record...");
        Message = "Deleting Autopilot record...";
        IncProgress();
        try
        {
            await graphClient.DeviceManagement.WindowsAutopilotDeviceIdentities[autopilotRecord.Id].Request()
                .DeleteAsync();
        }
        catch (ServiceException e)
        {
            Logger.LogError(e, "Got error trying to delete autopilot record id: {id}", autopilotRecord.Id);
            return new StepResult(false, "Failed to delete autopilot record");
        }


        return new StepResult(true, "Removed device from autopilot");
    }
}