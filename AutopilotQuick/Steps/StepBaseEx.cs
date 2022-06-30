using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Nito.AsyncEx;
using NLog;

namespace AutopilotQuick.Steps
{
    public abstract class StepBaseEx : StepBase
    {
        public void WaitForPause(PauseToken pauseToken)
        {
            var oldStatus = Status;
            if (!pauseToken.IsPaused) return;
            Message = "Waiting to resume";
            pauseToken.WaitWhilePaused();
            Status = oldStatus;
        }

        public void CountDown(PauseToken pauseToken, double ms)
        {
            var oldStatus = Status;
            IsIndeterminate = false;
            DateTime StartTime = DateTime.UtcNow;
            while ((DateTime.UtcNow - StartTime).TotalMilliseconds <= ms)
            {
                WaitForPause(pauseToken);
                Progress = ((DateTime.UtcNow - StartTime).TotalMilliseconds / ms) * 100;
                if (Progress <= 0)
                {
                    Progress = 100;
                }
                Thread.Sleep((int)Math.Round(ms/1000));
            }

            Status = oldStatus with{Progress = 100};
        }

        public string InvokePowershellScriptAndGetResult(string script)
        {
            var psscriptPath = Path.Join(Path.GetDirectoryName(App.GetExecutablePath()), "script.ps1");
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
            return output;
        }

        public string RunDiskpartScript(string Script)
        {
            var diskpartScriptPath = Path.Join(Path.GetDirectoryName(App.GetExecutablePath()), "diskpart.txt");
            try
            {
                File.WriteAllText(diskpartScriptPath, Script);
            }
            catch (IOException e)
            {
                //The file is being used by diskpart. We need to wait until diskpart has closed and retry
                var processes = System.Diagnostics.Process.GetProcessesByName("diskpart");
                foreach (var process in processes)
                {
                    process.WaitForExit(10000);
                    if (!process.HasExited)
                    {
                        process.Kill(true);
                    }

                    return RunDiskpartScript(Script);
                }
            }
            
            Process diskpartProcess = new Process();
            diskpartProcess.StartInfo.FileName = "diskpart.exe";
            diskpartProcess.StartInfo.UseShellExecute = false;
            diskpartProcess.StartInfo.RedirectStandardOutput = true;
            diskpartProcess.StartInfo.Arguments = $"/s {diskpartScriptPath}";
            diskpartProcess.StartInfo.CreateNoWindow = true;
            diskpartProcess.Start();
            var output = diskpartProcess.StandardOutput.ReadToEnd();
            diskpartProcess.WaitForExit();
            File.Delete(diskpartScriptPath);
            return output;
        }
    }
}
