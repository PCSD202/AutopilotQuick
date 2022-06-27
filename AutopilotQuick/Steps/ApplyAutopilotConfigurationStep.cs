using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Nito.AsyncEx;
using NLog;

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
                if (!context.TakeHomeToggleOn)
                {
                    await using var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("AutopilotQuick.Resources.AutopilotConfigurationFile.json");
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
