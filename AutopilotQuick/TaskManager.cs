using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.ObjectModel;
using Newtonsoft.Json;
using System.Diagnostics;
using System.IO;
using NLog;

namespace AutopilotQuick
{
    class TaskManager
    {
        private static readonly TaskManager instance = new();
        public static TaskManager getInstance()
        {
            return instance;
        }
        public event EventHandler<CurrentTaskNameChangedEventArgs> CurrentTaskNameChanged;
        public event EventHandler<CurrentTaskMessageChangedEventArgs> CurrentTaskMessageChanged;
        public event EventHandler<CurrentTaskProgressChangedEventArgs> CurrentTaskProgressChanged;
        public event EventHandler<TotalTaskProgressChangedEventArgs> TotalTaskProgressChanged;
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private void InvokeTotalTaskProgressChanged(string stepMessage, double progress, bool isIndeterminate)
        {
            TotalTaskProgressChanged?.Invoke(this, new TotalTaskProgressChangedEventArgs()
            {
                StepMessage = stepMessage,
                Progress = progress,
                isIndeterminate = isIndeterminate
            });
        }
        private void InvokeCurrentTaskNameChanged(string newName)
        {
            CurrentTaskNameChanged?.Invoke(this, new CurrentTaskNameChangedEventArgs()
            {
                Name = newName
            });
        }
        private void InvokeCurrentTaskMessageChanged(string newMessage)
        {
            CurrentTaskMessageChanged?.Invoke(this, new CurrentTaskMessageChangedEventArgs()
            {
                Message = newMessage
            });
        }
        private void InvokeCurrentTaskProgressChanged(double newProgressPercent, bool isIndeterminate = false)
        {
            CurrentTaskProgressChanged?.Invoke(this, new CurrentTaskProgressChangedEventArgs()
            {
                isIndeterminate = isIndeterminate,
                Progress = newProgressPercent
            });
        }
        public static WimCacher wimCache;
        public bool UpdatedImageAvailable = false;

        public bool Enabled = false;
        public bool FormatStep()
        {
            InvokeCurrentTaskNameChanged("Formatting drive");
            if (!Enabled)
            {
                try
                {
                    DateTime StartTime = DateTime.UtcNow;
                    InvokeCurrentTaskMessageChanged("Disabled (Not going to run, will continue after 5 seconds)");
                    
                    while ((DateTime.UtcNow - StartTime).TotalSeconds <= 5)
                    {
                        InvokeCurrentTaskProgressChanged(((DateTime.UtcNow - StartTime).TotalSeconds / 5) * 100);
                    }
                    return true;
                }
                catch (Exception e){
                    InvokeCurrentTaskMessageChanged("An error occured: "+e.Message);
                    return false;
                }

            }
            if (Enabled)
            {
                InvokeCurrentTaskMessageChanged("Identifying drive");
                InvokeCurrentTaskProgressChanged(0, true);
                var psscript = @"
Import-Module OSD
$disk = Get-Disk | Where-Object {$_.Number -notin (Get-Disk.usb | Select-Object -ExpandProperty Number)}
$disk.number
";
                
                try
                {
                    var psscriptPath = Path.Join(Path.GetDirectoryName(App.GetExecutablePath()), "script.ps1");
                    File.WriteAllText(psscriptPath, psscript);
                    Process formatProcess = new Process();
                    formatProcess.StartInfo.FileName = "Powershell.exe";
                    formatProcess.StartInfo.UseShellExecute = false;
                    formatProcess.StartInfo.RedirectStandardOutput = true;
                    formatProcess.StartInfo.CreateNoWindow = true;
                    formatProcess.StartInfo.Arguments = psscriptPath;
                    formatProcess.Start();
                    var diskNum = formatProcess.StandardOutput.ReadToEnd().Trim();
                    formatProcess.WaitForExit();
                    InvokeCurrentTaskProgressChanged(50, false);
                    var diskpartScript = $@"
select disk {diskNum}
clean
convert gpt
rem == 1. System partition =========================
create partition efi size=100
rem    ** NOTE: For Advanced Format 4Kn drives,
rem               change this value to size = 260 ** 
format quick fs=fat32 label='System'
assign letter = 'S'
rem == 2.Microsoft Reserved(MSR) partition =======
create partition msr size = 16
rem == 3.Windows partition ========================
rem == a.Create the Windows partition ==========
create partition primary
rem == b.Create space for the recovery tools ===
rem * *Update this size to match the size of
rem          the recovery tools(winre.wim)
rem          plus some free space.
shrink minimum = 500
rem == c.Prepare the Windows partition =========
format quick fs = ntfs label = 'Windows'
assign letter = 'W'
rem === 4.Recovery partition ======================
create partition primary
format quick fs = ntfs label = 'Recovery'
assign letter = 'R'
set id = 'de94bba4-06d1-4d40-a16a-bfd50179d6ac'
gpt attributes = 0x8000000000000001
exit
";
                    InvokeCurrentTaskMessageChanged($"Identified drive {diskNum}, running diskpart");
                    var diskpartScriptPath = Path.Join(Path.GetDirectoryName(App.GetExecutablePath()), "diskpart.txt");
                    File.WriteAllText(diskpartScriptPath, diskpartScript);
                    Process diskpartProcess = new Process();
                    diskpartProcess.StartInfo.FileName = "diskpart.exe";
                    diskpartProcess.StartInfo.UseShellExecute = false;
                    diskpartProcess.StartInfo.RedirectStandardOutput = true;
                    diskpartProcess.StartInfo.Arguments = $"/s {diskpartScriptPath}";
                    diskpartProcess.StartInfo.CreateNoWindow = true;
                    diskpartProcess.Start();
                    var diskpartOutput = diskpartProcess.StandardOutput.ReadToEnd();
                    Logger.Info($"Diskpart output: {diskpartOutput}");
                    diskpartProcess.WaitForExit();
                    if(diskpartOutput != null && diskpartOutput.Split(" ").Count(x=>x.ToLower() == "successfully" || x.ToLower() == "succeeded") == 14)
                    {
                        InvokeCurrentTaskMessageChanged($"Successfully formated drive {diskNum}");
                        InvokeCurrentTaskProgressChanged(100, false);
                        return true;
                    }
                    InvokeCurrentTaskMessageChanged($"Failed to format disk, check logs.");
                    InvokeCurrentTaskProgressChanged(100, false);
                    return false;
                } catch (Exception e)
                {
                    InvokeCurrentTaskMessageChanged("Error: " + e.Message);
                    return false;
                }
               


            }
            return false;
        }


        public void Run(UserDataContext context)
        {
            wimCache = new WimCacher("http://sccm2.psd202.org/WIM/21H2-install.wim", context);
            InternetMan.getInstance().InternetBecameAvailable += TaskManager_InternetBecameAvailable;
            FormatStep();
        }

        private void TaskManager_InternetBecameAvailable(object? sender, EventArgs e)
        {
            UpdatedImageAvailable = !wimCache.IsUpToDate();
        }
    }
    public class CurrentTaskNameChangedEventArgs : EventArgs
    {
        public string Name;
    }
    public class CurrentTaskMessageChangedEventArgs : EventArgs
    {
        public string Message;
    }
    public class CurrentTaskProgressChangedEventArgs : EventArgs
    {
        public double Progress;
        public bool isIndeterminate;
    }
    public class TotalTaskProgressChangedEventArgs : EventArgs
    {
        public string StepMessage;
        public double Progress;
        public bool isIndeterminate;
    }

}
