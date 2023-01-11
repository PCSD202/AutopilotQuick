#region

using System;
using System.Threading.Tasks;
using AutopilotQuick.LogMan;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Extensions.Logging;
using Nito.AsyncEx;

#endregion

namespace AutopilotQuick.Steps;

public class FinalizeSyncingLogsStep : StepBaseEx
{
    public override string Name() => "Finalize log syncing step";
    public readonly ILogger Logger = App.GetLogger<FinalizeSyncingLogsStep>();
    public override async Task<StepResult> Run(UserDataContext context, PauseToken pauseToken,
        IOperationHolder<RequestTelemetry> StepOperation)
    {
        Title = "Synchronizing logs with azure";
        Message = "Checking for internet";
        Progress = 0;
        DurableAzureBackgroundTask.getInstance().Stop();
        if (!InternetMan.CheckForInternetConnection())
        {
            Message = "Internet is not available. Logs will be uploaded next time";
            Progress = 100;
            return new StepResult(true, "Skipped synchronization due to no internet");
        }

        Progress = 25;
        Message = "Waiting for log service to shutdown...";
        var startTime = DateTime.UtcNow;
        while (((DateTime.UtcNow - startTime).TotalSeconds <= 5) && !DurableAzureBackgroundTask.getInstance().Stopped)
        {
            DurableAzureBackgroundTask.getInstance().Stop();
            await Task.Delay(250);
        }

        Progress = 75;
        Message = "Doing one final log sync...";
        if (((DateTime.UtcNow - startTime).TotalSeconds <= 10))
        {
            try
            {
                DurableAzureBackgroundTask.getInstance().SyncLogs();
            }
            catch (Exception e) { Logger.LogError(e, "Caught error while finalizing log sync"); }
        }
        
        

        Progress = 100;
        Message = "Finished";
        return new StepResult(true, "Synced logs and shutdown log sync service");
    }
}