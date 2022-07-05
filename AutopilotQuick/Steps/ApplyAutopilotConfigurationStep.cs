using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AutopilotQuick.WMI;
using Nito.AsyncEx;
using NLog;
using ORMi;

namespace AutopilotQuick.Steps
{
    internal class ApplyAutopilotConfigurationStep : StepBaseEx
    {
        public readonly Logger Logger = LogManager.GetCurrentClassLogger();
        public override async Task<StepResult> Run(UserDataContext context, PauseToken pauseToken)
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
                    WMIHelper helper = new WMIHelper("root\\CimV2");
                    var model = helper.QueryFirstOrDefault<ComputerSystem>().Model;
                    if (model == "Precision 7560")
                    {
                        resourceLocation = "AutopilotQuick.Resources.sharedpc.json";
                        Message = "Copying sharedpc autopilot config to windows";
                        Thread.Sleep(3000);
                    }
                    await using var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceLocation);
                    await using var file = new FileStream(@"W:\windows\Provisioning\Autopilot\AutopilotConfigurationFile.json", FileMode.Create, FileAccess.Write);
                    if (resource != null)
                    {
                        await resource.CopyToAsync(file);
                    }
                    else
                    {
                        Logger.Error("Could not find the autopilot config internally. This is a issue.");
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
