using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Nito.AsyncEx;
using NLog;

namespace AutopilotQuick.Steps
{
    internal class ApplyWifiStep : StepBaseEx
    {
        public readonly Logger Logger = LogManager.GetCurrentClassLogger();
        public override async Task<StepResult> Run(UserDataContext context, PauseToken pauseToken)
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
                Directory.CreateDirectory(DismTempDir);
                await using var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("AutopilotQuick.Resources.Wifi.ppkg");
                await using var file = new FileStream(Path.Combine(DismTempDir, "Wifi.ppkg"), FileMode.Create, FileAccess.Write);
                await resource.CopyToAsync(file);

                var output = await InvokePowershellScriptAndGetResultAsync(@$"DISM /Image=W:\ /Add-ProvisioningPackage /PackagePath:{Path.Combine(DismTempDir, "Wifi.ppkg")}", CancellationToken.None);
                Logger.Debug($"Apply Wifi step: {output}");
            }

            return new StepResult(true, "Successfully applied wifi settings");
        }
    }
}
