using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Navigation;
using AutopilotQuick.WMI;
using Humanizer;
using Nito.AsyncEx;
using NLog;
using ORMi;
using Polly;
using Polly.Timeout;

namespace AutopilotQuick.Steps
{
    internal class ApplyDellBiosSettingsStep : StepBaseEx
    {
        public readonly Logger Logger = LogManager.GetCurrentClassLogger();
        public override async Task<StepResult> Run(UserDataContext context, PauseToken pauseToken)
        {
            Title = "Applying dell bios settings";
            if (IsEnabled)
            {
                
                Message = "Extracting dell bios application";
                var dellBiosSettingsDir = Path.Combine(Path.GetDirectoryName(App.GetExecutablePath()), "Cache", "DellBiosSettings");
                Directory.CreateDirectory(dellBiosSettingsDir);
                Progress = 0;
                IsIndeterminate = true;

                //Copy all of our files from Resources/DellBiosSettings to a directory to execute
                var files = Assembly.GetExecutingAssembly().GetManifestResourceNames();
                foreach (var fileName in files.Where(x => x.Contains("DellBiosSettings"))) {
                    await using var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream(fileName);
                    await using var file = new FileStream(Path.Combine(dellBiosSettingsDir, fileName.Replace("AutopilotQuick.Resources.DellBiosSettings.", "")), FileMode.Create, FileAccess.Write);
                    await resource?.CopyToAsync(file)!;
                }

                IsIndeterminate = false;
                Progress = 25;
                
                Message = "Figuring out model";
                var scriptExecutable = "LaptopBiosSettings.cmd";
                WMIHelper helper = new WMIHelper("root\\CimV2");
                var model = helper.QueryFirstOrDefault<ComputerSystem>().Model;
                if (model.Contains("Optiplex"))
                {
                    scriptExecutable = "DesktopBiosSettings.cmd";
                }
                if (context.TakeHomeToggleOn)
                {
                    scriptExecutable = "cctk.exe --setuppwd= --valsetuppwd=PCSD202";
                }
                var script = @$"
cd {dellBiosSettingsDir}
& .\{scriptExecutable}
";
                Message = $"This device is a {model}, applying bios settings";
                var timeoutPolicy = Policy.TimeoutAsync(1.Minutes(), async (context1, timespan, task) =>
                {
                    Logger.Error($"{context1.PolicyKey}: execution timed out after {timespan.TotalSeconds} seconds.");
                    return;
                });
                string output = "";
                try
                {
                    output = await timeoutPolicy.ExecuteAsync(
                        async ct => await InvokePowershellScriptAndGetResultAsync(script, ct),
                        CancellationToken.None);
                }
                catch (TimeoutRejectedException)
                {
                    Logger.Error("Task exceeded timeout");
                }
                Logger.Debug($"Dell bios output: {Regex.Replace(output, @"^\s*$\n|\r", string.Empty, RegexOptions.Multiline).TrimEnd()}");
                Progress = 50;
                Message = "Cleaning up";
                Directory.Delete(dellBiosSettingsDir, true);
                Message = "Done";
                Progress = 100;
            }
            else
            {
                Title = "Applying dell bios settings - DISABLED";
                Message = "Will continue after 5 seconds";
                await Task.Run(() => CountDown(pauseToken, 5000));
            }
            return new StepResult(true, "Successfully applied bios settings");
        }
    }
}
