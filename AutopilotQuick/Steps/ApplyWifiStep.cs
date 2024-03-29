﻿#region

using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Extensions.Logging;
using Nito.AsyncEx;

#endregion

namespace AutopilotQuick.Steps
{
    internal class ApplyWifiStep : StepBaseEx
    {
        public override string Name() => "Apply wifi step";
        public readonly ILogger Logger = App.GetLogger<ApplyWifiStep>();
        public override async Task<StepResult> Run(UserDataContext context, PauseToken pauseToken,
            IOperationHolder<RequestTelemetry> StepOperation)
        {
            if (!IsEnabled)
            {
                Title = "Applying WiFi configuration - DISABLED";
                Message = "Will continue after 5 seconds";
                await Task.Run(() => CountDown(pauseToken, 5000));
                return new StepResult(true, "Finished applying wifi (dry)");
            }

            if (!context.TakeHomeToggleOn)
            {
                Title = "Applying WiFi configuration";
                Message = "";
                var DismTempDir = Path.Combine(Path.GetDirectoryName(App.GetExecutablePath()), "Cache", "TempDism");
                await context.WaitForDriveAsync(); //Wait for the drive to be present
                Directory.CreateDirectory(DismTempDir);
                await using var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("AutopilotQuick.Resources.Wifi.ppkg");
                await using var file = new FileStream(Path.Combine(DismTempDir, "Wifi.ppkg"), FileMode.Create, FileAccess.Write);
                await context.WaitForDriveAsync(); //Wait for the drive to be present
                await resource.CopyToAsync(file);

                var output = await InvokePowershellScriptAndGetResultAsync(@$"DISM /Image=W:\ /Add-ProvisioningPackage /PackagePath:{Path.Combine(DismTempDir, "Wifi.ppkg")}", CancellationToken.None);
                Logger.LogDebug("Apply Wifi step: {output}", output);
                StepOperation.Telemetry.Properties["Output"] = output;
            }

            return new StepResult(true, "Successfully applied wifi settings");
        }
    }
}
