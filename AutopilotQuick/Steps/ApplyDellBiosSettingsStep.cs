using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Navigation;
using AutopilotQuick.WMI;
using Humanizer;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Extensions.Logging;
using Nito.AsyncEx;
using ORMi;
using Polly;
using Polly.Timeout;

namespace AutopilotQuick.Steps
{
    internal class ApplyDellBiosSettingsStep : StepBaseEx
    {
        public override string Name() => "Apply dell bios settings step";
        public readonly ILogger Logger = App.GetLogger<ApplyDellBiosSettingsStep>();

        public override async Task<StepResult> Run(UserDataContext context, PauseToken pauseToken,
            IOperationHolder<RequestTelemetry> StepOperation)
        {
            Title = "Applying dell bios settings";
            if (!IsEnabled)
            {
                Title = "Applying dell bios settings - DISABLED";
                Message = "Will continue after 5 seconds";
                await Task.Run(() => CountDown(pauseToken, 5000));
                return new StepResult(true, "Skipped applying bios settings");
            }

            Message = "Extracting dell bios application";
            var dellBiosSettingsDir = Path.Combine(Path.GetDirectoryName(App.GetExecutablePath()), "Cache", "DellBiosSettings");

            Progress = 0;
            IsIndeterminate = true;

            Cacher DellBiosSettingsCacher =
                new Cacher("https://nettools.psd202.org/AutoPilotFast/DellBiosSettings.zip", "DellBiosSettings.zip",
                    context);
            using (var UpdateAndExtract = App.telemetryClient.StartOperation<RequestTelemetry>("Updating/Extracting dell bios"))
            {
                UpdateAndExtract.Telemetry.Properties["Downloaded"] = "false";
                //If the file is not cached, or if we have internet and the file is not up to date, or if the directory does not exist
                if (!DellBiosSettingsCacher.FileCached ||
                    (InternetMan.getInstance().IsConnected && !DellBiosSettingsCacher.IsUpToDate) ||
                    !Directory.Exists(dellBiosSettingsDir))
                {
                    UpdateAndExtract.Telemetry.Properties["Downloaded"] = "true";
                    if (Directory.Exists(dellBiosSettingsDir))
                    {
                        Directory.Delete(dellBiosSettingsDir, true);
                    }

                    Directory.CreateDirectory(dellBiosSettingsDir);
                    DellBiosSettingsCacher.DownloadUpdate();
                    ZipFile.ExtractToDirectory(DellBiosSettingsCacher.FilePath, dellBiosSettingsDir);
                }

                UpdateAndExtract.Telemetry.Success = true;
            }

            IsIndeterminate = false;
            Progress = 25;

            Message = "Figuring out model";
            var scriptExecutable = "LaptopBiosSettings.cmd";
            WMIHelper helper = new WMIHelper("root\\CimV2");
            var model = GetDeviceModel(pauseToken);
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
            Logger.LogInformation("Running {script} for model to apply settings", scriptExecutable);
            var timeoutPolicy = Policy.TimeoutAsync(1.Minutes(), async (context1, timespan, task) =>
            {
                Logger.LogError("{policy}: execution timed out after {seconds} seconds.", context1.PolicyKey,
                    timespan.TotalSeconds);
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
                Logger.LogError("Task exceeded timeout");
            }

            Logger.LogDebug("Dell bios output: {biosOutput}",
                Regex.Replace(output, @"^\s*$\n|\r", string.Empty, RegexOptions.Multiline).TrimEnd());
            Message = "Done";
            Progress = 100;
            return new StepResult(true, "Successfully applied bios settings");
        }
    }
}