﻿using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AQ.GroupManagementLibrary;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Nito.AsyncEx;

namespace AutopilotQuick.Steps;

public class MakeSureAutopilotSyncedStep : StepBaseEx
{
    public override string Name()
    {
        return "Autopilot Sync step";
    }

    private async Task<StepResult> RunSharedPCSync(GroupManagementClient client, UserDataContext context, PauseToken pauseToken)
    {
        var synced = false;
        try
        {
            var status = await client.CheckAutopilotProfileSyncStatus(GetServiceTag(pauseToken));
            if (status != null) synced = status.Value.synced;
        }
        catch (Exception e)
        {
            App.GetLogger<MakeSureAutopilotSyncedStep>()
                .LogError(e, "Got error while checking if autopilot profile synced {e}", e);
        }

        if (synced) return new StepResult(true, "Autopilot profile synced");
        
        var progressWindow =
            await context.DialogCoordinator.ShowProgressAsync(context, "Waiting for autopilot sync", 
                "This is to make sure that when the computer is first turned on, the right Autopilot profile is applied. This usually takes only a few minutes but can take upwards of 30." +
                " If you do not want to wait for this, you can cancel with the button below. If the wrong autopilot profile is applied, you need to re-image the computer.",
                true);
        progressWindow.SetIndeterminate();


        var errorCount = 0;
        var cts = new CancellationTokenSource();
        progressWindow.Canceled += (sender, args) => cts.Cancel();
        while (!progressWindow.IsCanceled && !synced && errorCount < 5)
        {
            try
            {
                var status = await client.CheckAutopilotProfileSyncStatus(GetServiceTag(pauseToken));
                if (status.HasValue)
                {
                    synced = status.Value.synced;
                }

                if (!synced)
                {
                    await Task.Delay(5000, cts.Token);
                }
            } catch (Exception e) {
                App.GetLogger<MakeSureAutopilotSyncedStep>().LogError(e, "Got error while checking if autopilot profile synced {e}", e);
                errorCount++;
            }
        }
        await progressWindow.CloseAsync();

        return progressWindow.IsCanceled ? new StepResult(true, "Autopilot sync canceled") : new StepResult(true, "Autopilot profile synced");
    }
    
        private async Task<StepResult> RunTakeHomeSync(GroupManagementClient client, UserDataContext context, PauseToken pauseToken)
    {
        var exists = true;
        try
        {
            var status = await client.CheckAutopilotProfileExists(GetServiceTag(pauseToken));
            if (status != null) exists = status.Value.Exists;
        }
        catch (Exception e)
        {
            App.GetLogger<MakeSureAutopilotSyncedStep>()
                .LogError(e, "Got error while checking if autopilot profile exists {e}", e);
        }

        if (!exists) return new StepResult(true, "Autopilot profile does not exist");
        
        var progressWindow =
            await context.DialogCoordinator.ShowProgressAsync(context, "Waiting for autopilot profile deletion", 
                "This is to make sure that when the computer is first turned on, the right Autopilot profile is applied. This usually takes only a few minutes but can take upwards of 30." +
                " If you do not want to wait for this, you can cancel with the button below. If the wrong autopilot profile is applied, you need to re-image the computer.",
                true);
        progressWindow.SetIndeterminate();


        var errorCount = 0;
        var cts = new CancellationTokenSource();
        progressWindow.Canceled += (sender, args) => cts.Cancel();
        while (!progressWindow.IsCanceled && exists && errorCount < 5)
        {
            try
            {
                var status = await client.CheckAutopilotProfileExists(GetServiceTag(pauseToken));
                if (status.HasValue)
                {
                    exists = status.Value.Exists;
                }

                if (exists)
                {
                    await Task.Delay(5000, cts.Token);
                }
            } catch (Exception e) {
                App.GetLogger<MakeSureAutopilotSyncedStep>().LogError(e, "Got error while checking if autopilot profile exists {e}", e);
                errorCount++;
            }
        }
        await progressWindow.CloseAsync();

        return progressWindow.IsCanceled ? new StepResult(true, "Autopilot exists canceled") : new StepResult(true, "Autopilot does not exist");
    }

    public override async Task<StepResult> Run(UserDataContext context, PauseToken pauseToken,
        IOperationHolder<RequestTelemetry> StepOperation)
    {
        Title = "Autopilot sync step";
        Message = "Making sure autopilot is synced";
        IsIndeterminate = true;
        if (!IsEnabled || !InternetMan.GetInstance().IsConnected || context.TakeHomeToggleOn)
        {
            await CountDown(pauseToken, 5000);
            return new StepResult(true, "Autopilot sync step disabled");
        }

        var groupManConfigCache = new Cacher(CachedResourceUris.GroupManConfig, context);
        if (!groupManConfigCache.IsUpToDate || !groupManConfigCache.FileCached)
        {
            await Task.Run(async () => await groupManConfigCache.DownloadUpdateAsync());
        }

        var config = JsonConvert.DeserializeObject<MainWindow.GroupManConfig>(await groupManConfigCache.ReadAllTextAsync());
        var client = new GroupManagementClient(App.GetLogger<GroupManagementClient>(), config.APIKEY, config.URL);
        if (!context.TakeHomeToggleOn)
        {
            return await RunSharedPCSync(client, context, pauseToken);
        }
        else
        {
            return await RunTakeHomeSync(client, context, pauseToken);
        }
        

    }
}