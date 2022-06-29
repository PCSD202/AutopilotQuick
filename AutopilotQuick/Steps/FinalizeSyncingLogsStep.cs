﻿using System;
using System.Threading;
using System.Threading.Tasks;
using AutopilotQuick.LogMan;
using Nito.AsyncEx;
using NLog;

namespace AutopilotQuick.Steps;

public class FinalizeSyncingLogsStep : StepBaseEx
{
    public readonly Logger Logger = LogManager.GetCurrentClassLogger();
    public override async Task<StepResult> Run(UserDataContext context, PauseToken pauseToken)
    {
        Title = "Synchronizing logs with azure";
        Message = "Checking for internet";
        Progress = 0;
        DurableAzureBackgroundTask.getInstance().ShouldStop = true;
        if (!InternetMan.CheckForInternetConnection())
        {
            Message = "Internet is not available. Logs will be uploaded next time";
            Progress = 100;
            return new StepResult(true, "Skipped synchronization due to no internet");
        }

        Progress = 25;
        Message = "Waiting for log service to shutdown...";
        while (!DurableAzureBackgroundTask.getInstance().Stopped)
        {
            DurableAzureBackgroundTask.getInstance().ShouldStop = true;
            Thread.Sleep(250);
        }

        Progress = 75;
        Message = "Doing one final log sync...";
        try
        {
            DurableAzureBackgroundTask.getInstance().SyncLogs();
        }
        catch (Exception e) { Logger.Error(e); }
        

        Progress = 100;
        Message = "Finished";
        return new StepResult(true, "Synced logs and shutdown log sync service");
    }
}