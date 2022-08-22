using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using AutopilotQuick.WMI;
using Microsoft.ApplicationInsights;
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
    public readonly ILogger Logger = App.GetLogger<RemoveDeviceFromAutopilotStep>();
    
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
        
        
        
        Message = "Looking up service tag...";
        IncProgress();
        var serviceTag = GetServiceTag(pauseToken);


        var CredentialStep = App.telemetryClient.StartOperation<RequestTelemetry>("Loading credentials");
        WaitWhilePaused(pauseToken);
        Message = "Loading credentials...";
        IncProgress();
        //Gets the decrypted credentials from the encrypted file
        var GraphCreds = await GraphHelper.GetGraphCreds(context);
        Logger.LogInformation($"Credentials Loaded");
        CredentialStep.Telemetry.Success = true;
        CredentialStep.Dispose();
        
        var MicrosoftGraphStep = App.telemetryClient.StartOperation<RequestTelemetry>("Connecting to MsGraph");
        WaitWhilePaused(pauseToken);
        Message = "Connecting to Microsoft Graph...";
        IncProgress();
        var graphClient = GraphHelper.ConnectToMSGraph(GraphCreds);
        Logger.LogInformation($"Connected to MSGraph");
        MicrosoftGraphStep.Telemetry.Success = true;
        MicrosoftGraphStep.Dispose();
        
        var LookupAutopilotRecord = App.telemetryClient.StartOperation<RequestTelemetry>("Looking up device autopilot record");
        WaitWhilePaused(pauseToken);
        Message = $"Looking up ({serviceTag})'s autopilot record...";
        IncProgress();
        Logger.LogInformation($"Looking up autopilot record for device");
        var autopilotRecord = await GraphHelper.GetWindowsAutopilotDevice(serviceTag, graphClient, Logger);
        LookupAutopilotRecord.Telemetry.Success = autopilotRecord is not null;
        LookupAutopilotRecord.Telemetry.Properties["AutopilotRecord"] = JsonConvert.SerializeObject(autopilotRecord);
        
        if (autopilotRecord is null)
        {
            Progress = 100;
            Message = "No autopilot record found for device";
            LookupAutopilotRecord.Dispose();
            return new StepResult(true, "No autopilot record found for device");
        }
        Logger.LogInformation("Found autopilot record for device: {@autopilotRecord}", autopilotRecord);
        LookupAutopilotRecord.Dispose();
        
        using (var operation = App.telemetryClient.StartOperation<RequestTelemetry>("Looking up device intune record"))
        {
            WaitWhilePaused(pauseToken);
            Message = $"Looking up ({serviceTag})'s intune object...";
            IncProgress();
            Logger.LogInformation("Looking up intune object with id: {id}", autopilotRecord.ManagedDeviceId);
            var intuneObject = await GraphHelper.GetIntuneObject(autopilotRecord.ManagedDeviceId, graphClient, Logger);
            operation.Telemetry.Success = intuneObject is not null;
            operation.Telemetry.Properties["IntuneRecord"] = JsonConvert.SerializeObject(intuneObject);

            if (intuneObject is null)
            {
                Progress = 100;
                Message = "No intune object found for device";
                return new StepResult(true, "No intune object found for device");
            }

            Logger.LogInformation("Found intune object for device: {@intuneObject}", intuneObject);
            operation.Dispose();
        }

        using (var DeleteIntuneRecord = App.telemetryClient.StartOperation<RequestTelemetry>("Deleting intune record"))
        {
            WaitWhilePaused(pauseToken);
            Message = "Deleting intune object...";
            IncProgress();
            DeleteIntuneRecord.Telemetry.Properties["ID"] = autopilotRecord.ManagedDeviceId;
            try
            {
                Logger.LogInformation($"Deleting intune object...");
                await graphClient.DeviceManagement.ManagedDevices[autopilotRecord.ManagedDeviceId].Request()
                    .DeleteAsync();
                DeleteIntuneRecord.Telemetry.Success = true;
            }
            catch (ServiceException e)
            {
                Logger.LogError(e, "Got error trying to delete intune object id: {id}",
                    autopilotRecord.ManagedDeviceId);
                DeleteIntuneRecord.Telemetry.Success = false;
            }
        }

        using (var DeleteAutopilotRecord = App.telemetryClient.StartOperation<RequestTelemetry>("Deleting autopilot record"))
        {
            Logger.LogInformation("Deleting Autopilot record...");
            Message = "Deleting Autopilot record...";
            IncProgress();
            DeleteAutopilotRecord.Telemetry.Properties["AutopilotID"] = autopilotRecord.Id; 
            try
            {
                await graphClient.DeviceManagement.WindowsAutopilotDeviceIdentities[autopilotRecord.Id].Request()
                    .DeleteAsync();
                DeleteAutopilotRecord.Telemetry.Success = true;
            }
            catch (ServiceException e)
            {
                DeleteAutopilotRecord.Telemetry.Success = false;
                Logger.LogError(e, "Got error trying to delete autopilot record id: {id}", autopilotRecord.Id);
                return new StepResult(false, "Failed to delete autopilot record");
            }


            return new StepResult(true, "Removed device from autopilot");
        }
    }
}