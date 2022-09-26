using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AutopilotQuick.WMI;
using Humanizer;
using LazyCache;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Nito.AsyncEx;
using NLog;
using ORMi;
using Polly;
using Polly.Retry;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace AutopilotQuick.Steps
{
    public abstract class StepBaseEx : StepBase
    {
        public override double ProgressWeight() => 1;

        private readonly IAppCache _appCache = new CachingService();
        
        public void WaitWhilePaused(PauseToken pauseToken) {
            if (!pauseToken.IsPaused) return;
            using (App.telemetryClient.StartOperation<RequestTelemetry>("Paused"))
            {
                var oldStatus = Status;
                Message = "Paused, waiting to resume";
                IsIndeterminate = true;
                pauseToken.WaitWhilePaused();
                Status = oldStatus;
            }
        }

        [Obsolete(message:"Use DeviceInfoHelper.DeviceModel")]
        public string GetDeviceModel(PauseToken pauseToken) {
            WaitWhilePaused(pauseToken);
            return DeviceInfoHelper.DeviceModel;
        }
        
        [Obsolete(message:"Use DeviceInfoHelper.ServiceTag")]
        public string GetServiceTag(PauseToken pauseToken) {
            WaitWhilePaused(pauseToken);
            return DeviceInfoHelper.ServiceTag;
        }

        public async Task CountDown(PauseToken pauseToken, double ms)
        {
            using (App.GetTelemetryClient().StartOperation<RequestTelemetry>("Countdown"))
            {
                var oldStatus = Status;
                IsIndeterminate = false;
                var sw = Stopwatch.StartNew();
                while (sw.ElapsedMilliseconds <= ms)
                {
                    sw.Stop();
                    WaitWhilePaused(pauseToken);
                    sw.Start();
                    
                    Progress = Math.Round((sw.ElapsedMilliseconds / ms) * 100, 2);
                    if (Progress < 0)
                    {
                        Progress = 100;
                    }

                    //Thread.Sleep((int)Math.Round(ms/200,0));
                    await Task.Delay(249).ConfigureAwait(false);
                }

                Status = oldStatus with { Progress = 100 };
            }
        }
        
        public string InvokePowershellScriptAndGetResult(string script)
        {
            using (var powershellTele = App.GetTelemetryClient().StartOperation<RequestTelemetry>("Powershell script"))
            {
                var psscriptPath = Path.Join(Path.GetDirectoryName(App.GetExecutablePath()),
                    $"script-{Guid.NewGuid()}.ps1");
                File.WriteAllText(psscriptPath, script);
                Process formatProcess = new Process();
                formatProcess.StartInfo.FileName = "Powershell.exe";
                formatProcess.StartInfo.UseShellExecute = false;
                formatProcess.StartInfo.RedirectStandardOutput = true;
                formatProcess.StartInfo.CreateNoWindow = true;
                formatProcess.StartInfo.Arguments = psscriptPath;
                formatProcess.Start();
                var output = formatProcess.StandardOutput.ReadToEnd().Trim();
                formatProcess.WaitForExit();
                File.Delete(psscriptPath);
                powershellTele.Telemetry.Properties["Output"] = output;
                return output;
            }
        }
        
        public async Task<string> InvokePowershellScriptAndGetResultAsync(string script, CancellationToken cancellationToken)
        {
            using (var powershellTele = App.GetTelemetryClient().StartOperation<RequestTelemetry>("Powershell script"))
            {
                var lines = new StringBuilder();
                var psscriptPath = Path.Join(Path.GetDirectoryName(App.GetExecutablePath()),
                    $"script-{Guid.NewGuid()}.ps1");
                await File.WriteAllTextAsync(psscriptPath, script, cancellationToken);
                Process powerShellProcess = new Process();
                powerShellProcess.StartInfo.FileName = "Powershell.exe";
                powerShellProcess.StartInfo.UseShellExecute = false;
                powerShellProcess.StartInfo.RedirectStandardOutput = true;
                powerShellProcess.StartInfo.CreateNoWindow = true;
                powerShellProcess.StartInfo.Arguments = psscriptPath;
                powerShellProcess.Start();
                ILogger logger = App.GetLogger<StepBaseEx>();
                try
                {
                    while (!powerShellProcess.StandardOutput.EndOfStream)
                    {
                        var line = await powerShellProcess.StandardOutput.ReadLineAsync();
                        lines.AppendLine(line);
                    }
                    await powerShellProcess.WaitForExitAsync(cancellationToken);
                    powershellTele.Telemetry.Properties["Output"] = lines.ToString().Trim();
                    return lines.ToString().Trim();
                }
                finally
                {
                    if (!powerShellProcess.HasExited)
                    {
                        powerShellProcess.Kill();
                    }

                    File.Delete(psscriptPath);
                }
            }

        }
        
        public string RunDiskpartScript(string Script)
        {
            using (var diskpartScriptTele = App.GetTelemetryClient().StartOperation<RequestTelemetry>("Diskpart script"))
            {
                var diskpartScriptPath = Path.Join(Path.GetDirectoryName(App.GetExecutablePath()),
                    $"diskpart-(${Guid.NewGuid()}).txt");
                RetryPolicy retry = Policy
                    .Handle<IOException>()
                    .WaitAndRetry(60, retryAttempt => TimeSpan.FromSeconds(5));
                retry.Execute(() => { File.WriteAllText(diskpartScriptPath, Script); });

                //Shutdown diskpart if it is already open
                var processes = System.Diagnostics.Process.GetProcessesByName("diskpart");
                foreach (var process in processes)
                {
                    process.WaitForExit(10000);
                    if (!process.HasExited)
                    {
                        process.Kill();
                    }
                }

                Process diskpartProcess = new Process();
                diskpartProcess.StartInfo.FileName = "diskpart.exe";
                diskpartProcess.StartInfo.UseShellExecute = false;
                diskpartProcess.StartInfo.RedirectStandardOutput = true;
                diskpartProcess.StartInfo.Arguments = $"/s {diskpartScriptPath}";
                diskpartProcess.StartInfo.CreateNoWindow = true;
                diskpartProcess.StartInfo.RedirectStandardInput = true;
                diskpartProcess.Start();
                var output = diskpartProcess.StandardOutput.ReadToEnd();
                diskpartProcess.WaitForExit();
                File.Delete(diskpartScriptPath);
                diskpartScriptTele.Telemetry.Properties["Output"] = output;
                return output;
            }
        }
    }
}
