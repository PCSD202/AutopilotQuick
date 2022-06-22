using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Navigation;
using AutopilotQuick.WMI;
using Nito.AsyncEx;
using NLog;
using ORMi;

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
                foreach (var fileName in files.Where(x => x.Contains("DellBiosSettings")))
                {
                    using (var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream(fileName))
                    {
                        using (var file = new FileStream(Path.Combine(dellBiosSettingsDir, fileName.Replace("AutopilotQuick.Resources.DellBiosSettings.", "")), FileMode.Create, FileAccess.Write))
                        {

                            resource.CopyTo(file);
                        }
                    }
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
                var output = InvokePowershellScriptAndGetResult(script);
                Logger.Debug($"Dell bios output: {output}");
                Progress = 50;
                Message = "Cleaning up";
                Directory.Delete(dellBiosSettingsDir, true);
                Message = "Done";
                Progress = 100;
            }

            return new StepResult(true, "Successfully applied drivers");
        }
    }
}
