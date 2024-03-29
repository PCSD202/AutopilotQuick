﻿#region

using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Extensions.Logging;
using Nito.AsyncEx;

#endregion

namespace AutopilotQuick.Steps
{
    internal class ApplyAutopilotConfigurationStep : StepBaseEx
    {
        public override string Name() => "Apply autopilot configuration step";
        public readonly ILogger Logger = App.GetLogger<ApplyAutopilotConfigurationStep>();
        public override async Task<StepResult> Run(UserDataContext context, PauseToken pauseToken, IOperationHolder<RequestTelemetry> StepOperation)
        {
            Title = "Applying Autopilot configuration";
            Message = "";
            Progress = 0;
            IsIndeterminate = true;
            if (IsEnabled)
            {
                Message = "Copying autopilot config to windows";
                if (!context.TakeHomeToggleOn)
                {
                    var resourceLocation = "AutopilotQuick.Resources.AutopilotConfigurationFile.json";
                    try
                    {
                        await using var resource =
                            Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceLocation);
                        await using var file =
                            new FileStream(@"W:\windows\Provisioning\Autopilot\AutopilotConfigurationFile.json",
                                FileMode.Create, FileAccess.Write);
                        if (resource != null)
                        {
                            await resource.CopyToAsync(file);
                        }
                        else
                        {
                            Logger.LogError("Could not find the autopilot config internally. This is a issue.");
                        }
                    }
                    catch (DirectoryNotFoundException e)
                    {
                        return new StepResult(false, "Could not find W drive");
                    }
                    
                    
                }
                else
                {
                    Progress = 100;
                    return new StepResult(true, "Skipping application of autopilot config because of take-home");
                }
            }
            else
            {
                Title = "Applying Autopilot configuration - DISABLED";
                Message = "Will continue after 5 seconds";
                await Task.Run(() => CountDown(pauseToken, 5000));
            }
            
            Progress = 100;
            return new StepResult(true, "Successfully applied the autopilot configuration");
        }
    }
}
