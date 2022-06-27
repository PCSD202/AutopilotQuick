using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Diagnostics;
using System.IO;
using NLog;
using Microsoft.Wim;
using System.Reflection;
using Usb.Events;
using ORMi;
using AutopilotQuick.WMI;
using System.Threading.Tasks;
using System.Windows.Documents;
using AutopilotQuick.Steps;
using Nito.AsyncEx;

namespace AutopilotQuick
{
    class TaskManager {
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

        private void InvokeTotalTaskProgressChanged(double progress, bool isIndeterminate = false)
        {
            TotalTaskProgressChanged?.Invoke(this, new TotalTaskProgressChangedEventArgs()
            {
                StepMessage = "",
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
        public static Cacher wimCache;
        public bool UpdatedImageAvailable = false;
       

        public bool Enabled = false;
        public bool DriveRemoved = false;
        public bool RemoveOnly = false;

        public static string InvokePowershellScriptAndGetResult(string script)
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

        public int IdentifyDriveToImage()
        {
            InvokeCurrentTaskMessageChanged("Identifying drive to image");
            InvokeCurrentTaskProgressChanged(0, true);
            var psscript = @"
Import-Module OSD
$disk = Get-Disk | Where-Object {$_.Number -notin (Get-Disk.usb | Select-Object -ExpandProperty Number)}
$disk.number
";
            try
            {
                string diskNum = InvokePowershellScriptAndGetResult(psscript);
                int intDiskNum;
                bool success = int.TryParse(diskNum, out intDiskNum);
                if (success)
                {
                    InvokeCurrentTaskMessageChanged($"Identified drive {intDiskNum}");
                    InvokeCurrentTaskProgressChanged(100, false);
                    return intDiskNum;
                }
                else
                {
                    return -1;
                }
            }
            catch (Exception ex)
            {
                return -1;
            }
        }

        public string RunDiskpartScript(string Script)
        {
            var diskpartScriptPath = Path.Join(Path.GetDirectoryName(App.GetExecutablePath()), "diskpart.txt");
            File.WriteAllText(diskpartScriptPath, Script);
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

        public bool FormatDrive(int DriveToImage)
        {
            try
            {
                var diskpartScript = $@"
select disk {DriveToImage}
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
                InvokeCurrentTaskMessageChanged($"Identified drive {DriveToImage}, running diskpart");
                var diskpartOutput = RunDiskpartScript(diskpartScript);
                Logger.Debug($"Diskpart output: {diskpartOutput}");
                
                if (diskpartOutput != null && diskpartOutput.Split(" ").Count(x => x.ToLower() == "successfully" || x.ToLower() == "succeeded") >= 14)
                {
                    InvokeCurrentTaskMessageChanged($"Successfully formated drive {DriveToImage}");
                    InvokeCurrentTaskProgressChanged(100, false);
                    return true;
                }
                InvokeCurrentTaskMessageChanged($"Failed to format disk, check logs.");
                InvokeCurrentTaskProgressChanged(100, false);
                return false;
                
            }
            catch (Exception ex)
            {
                Logger.Error(ex);
                InvokeCurrentTaskMessageChanged($"Failed to format disk, check logs.");
                InvokeCurrentTaskProgressChanged(100, false);
                return false;
            }
            

        }

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
                try
                {
                    int DriveToImage = IdentifyDriveToImage();
                    if (DriveToImage == -1)
                    {
                        return false;
                    }
                    var result = FormatDrive(DriveToImage);
                    return result;
                }
                catch (Exception e)
                {
                    InvokeCurrentTaskMessageChanged("Error: " + e.Message);
                    Logger.Error(e);
                    return false;
                }
            }
            return false;
        }

        public bool ApplyImageStep()
        {
            InvokeCurrentTaskNameChanged("Applying windows image");
            InvokeCurrentTaskMessageChanged("Starting up...");
            
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
                catch (Exception e)
                {
                    InvokeCurrentTaskMessageChanged("An error occured: " + e.Message);
                    return false;
                }

            }
            if (Enabled)
            {
                try
                {
                    if (!wimCache.FileCached)
                    {
                        InternetMan.getInstance().InternetBecameAvailable -= TaskManager_InternetBecameAvailable;
                        UpdatedImageAvailable = false;
                        wimCache.DownloadUpdate();
                        InternetMan.getInstance().InternetBecameAvailable += TaskManager_InternetBecameAvailable;
                    }

                    using (var wimHandle = WimgApi.CreateFile(wimCache.FilePath, WimFileAccess.Read, WimCreationDisposition.OpenExisting, WimCreateFileOptions.None, WimCompressionType.None))
                    {
                        // Always set a temporary path
                        WimgApi.SetTemporaryPath(wimHandle, Environment.GetEnvironmentVariable("TEMP"));

                        // Register a method to be called while actions are performed by WIMGAPi for this .wim file
                        WimgApi.RegisterMessageCallback(wimHandle, ImageCallback);

                        try
                        {
                            // Get a handle to a specific image inside of the .wim
                            using (var imageHandle = WimgApi.LoadImage(wimHandle, 1))
                            {
                                // Apply the image
                                WimgApi.ApplyImage(imageHandle, "W:\\", WimApplyImageOptions.None);
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.Error(ex);
                        }
                        finally
                        {
                            // Be sure to unregister the callback method
                            //
                            WimgApi.UnregisterMessageCallback(wimHandle, ImageCallback);
                        }
                    }
                }
                catch (Exception e)
                {
                    Logger.Error(e);
                }
                
            }
            if (!wimCache.FileCached)
            {
                InternetMan.getInstance().InternetBecameAvailable -= TaskManager_InternetBecameAvailable;
                UpdatedImageAvailable = false;
                wimCache.DownloadUpdate();
                InternetMan.getInstance().InternetBecameAvailable += TaskManager_InternetBecameAvailable;
                return ApplyImageStep();
            }
            
            return true;
        }
        private WimMessageResult ImageCallback(WimMessageType messageType, object message, object userData)
        {
            // This method is called for every single action during the process being executed.
            // In the case of apply, you'll get Progress, Info, Warnings, Errors, etc
            //
            // The trick is to determine the message type and cast the "message" param to the corresponding type
            //

            switch (messageType)
            {
                case WimMessageType.Progress:  // Some progress is being sent

                    // Get the message as a WimMessageProgress object
                    WimMessageProgress progressMessage = (WimMessageProgress)message;

                    InvokeCurrentTaskMessageChanged($"Applying image {progressMessage.PercentComplete}%");
                    // Print the progress
                    InvokeCurrentTaskProgressChanged(progressMessage.PercentComplete);

                    break;

                case WimMessageType.Warning:  // A warning is being sent

                    // Get the message as a WimMessageProgress object
                    WimMessageWarning warningMessage = (WimMessageWarning)message;

                    // Print the file and error code
                    InvokeCurrentTaskMessageChanged($"Warning: {warningMessage.Path} ({warningMessage.Win32ErrorCode})");

                    break;

                case WimMessageType.Error:  // An error is being sent

                    // Get the message as a WimMessageError object
                    WimMessageError errorMessage = (WimMessageError)message;

                    // Print the file and error code
                    InvokeCurrentTaskMessageChanged($"Error: {errorMessage.Path} ({errorMessage.Win32ErrorCode})");
                    break;
            }

            // Depending on what this method returns, the WIMGAPI will continue or cancel.
            //
            // Return WimMessageResult.Abort to cancel.  In this case we return Success so WIMGAPI keeps going
            if (UpdatedImageAvailable)
            {
                return WimMessageResult.Abort;
            }
            return WimMessageResult.Success;
        }

        public bool MakeDiskBootable()
        {
            if (Enabled)
            {
                InvokeCurrentTaskNameChanged("Making disk bootable");
                InvokeCurrentTaskMessageChanged("");
                InvokeCurrentTaskProgressChanged(0, true);
                var script = @"
rem == Copy boot files to the System partition ==
W:\Windows\System32\bcdboot W:\Windows /s S:

:rem == Copy the Windows RE image to the
:rem    Windows RE Tools partition ==
md R:\Recovery\WindowsRE
xcopy /h W:\Windows\System32\Recovery\Winre.wim R:\Recovery\WindowsRE\

:rem == Register the location of the recovery tools ==
W:\Windows\System32\Reagentc /Setreimage /Path R:\Recovery\WindowsRE /Target W:\Windows

:rem == Verify the configuration status of the images. ==
W:\Windows\System32\Reagentc /Info /Target W:\Windows
";
                var output = InvokePowershellScriptAndGetResult(script);
            }
            return true;

        }

        public bool ApplyDellBiosSettings()
        {
            if (Enabled)
            {
                InvokeCurrentTaskNameChanged("Applying dell bios settings");
                InvokeCurrentTaskMessageChanged("Extracting dell bios application");
                var dellBiosSettingsDir = Path.Combine(Path.GetDirectoryName(App.GetExecutablePath()), "Cache", "DellBiosSettings");
                Directory.CreateDirectory(dellBiosSettingsDir);
                InvokeCurrentTaskProgressChanged(0, true);
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
                InvokeCurrentTaskProgressChanged(25, false);
                InvokeCurrentTaskMessageChanged("Figuring out if desktop or laptop");
                var scriptExecutable = "LaptopBiosSettings.cmd";
                WMIHelper helper = new WMIHelper("root\\CimV2");
                var model = helper.QueryFirstOrDefault<ComputerSystem>().Model;
                if (model.Contains("Optiplex"))
                {
                    scriptExecutable = "DesktopBiosSettings.cmd";
                }
                if (TakeHome)
                {
                    scriptExecutable = "cctk.exe --setuppwd= --valsetuppwd=PCSD202";
                }
                var script = @$"
cd {dellBiosSettingsDir}
& .\{scriptExecutable}
";
                if (scriptExecutable == "LaptopBiosSettings.cmd")
                {
                    InvokeCurrentTaskMessageChanged("This device is a laptop, applying settings");
                }
                else
                {
                    InvokeCurrentTaskMessageChanged("This device is a Desktop, applying settings");
                }
                var output = InvokePowershellScriptAndGetResult(script);
                Logger.Debug($"Dell bios output: {output}");
                InvokeCurrentTaskProgressChanged(50, false);
                InvokeCurrentTaskMessageChanged("Cleaning up");
                Directory.Delete(dellBiosSettingsDir, true);
                InvokeCurrentTaskMessageChanged("Done");
                InvokeCurrentTaskProgressChanged(100, false);
            }
            return true;
        }

        public bool ApplyWindowsAutopilotConfigurationStep()
        {
            InvokeCurrentTaskNameChanged("Applying Autopilot configuration");
            InvokeCurrentTaskMessageChanged("");
            InvokeCurrentTaskProgressChanged(0, true);
            using (var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("AutopilotQuick.Resources.AutopilotConfigurationFile.json"))
            {
                using (var file = new FileStream(@"W:\windows\Provisioning\Autopilot\AutopilotConfigurationFile.json", FileMode.Create, FileAccess.Write))
                {

                    resource.CopyTo(file);
                }
            }
            return true;
        }

        public bool ApplyWifiStep()
        {
            InvokeCurrentTaskNameChanged("Applying WiFi configuration");
            InvokeCurrentTaskMessageChanged("");
            var DismTempDir = Path.Combine(Path.GetDirectoryName(App.GetExecutablePath()), "Cache", "TempDism");
            Directory.CreateDirectory(DismTempDir);
            using (var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("AutopilotQuick.Resources.Wifi.ppkg"))
            {
                using (var file = new FileStream(Path.Combine(DismTempDir, "Wifi.ppkg"), FileMode.Create, FileAccess.Write))
                {

                    resource.CopyTo(file);
                }
            }
            var output = InvokePowershellScriptAndGetResult(@$"DISM /Image=W:\ /Add-ProvisioningPackage /PackagePath:{Path.Combine(DismTempDir, "Wifi.ppkg")}");
            Logger.Debug($"Apply Wifi step: {output}");

            return true;
        }


        public bool RemoveUnattendXMLStep()
        {
            InvokeCurrentTaskNameChanged("Removing Unattend.XML from panther");
            InvokeCurrentTaskMessageChanged("");
            InvokeCurrentTaskProgressChanged(0, true);
            try
            {
                File.Delete(@"W:\Windows\Panther\unattend\unattend.xml");
            } catch (Exception ex)
            {
                //Removal failed but it doesn't matter
            }
            return true;
            

        }

        public void Countdown(string title, string message, int seconds = 5, PauseToken pauseToken = new PauseToken()) {
            InvokeCurrentTaskNameChanged(title);
            InvokeCurrentTaskMessageChanged(message);
            DateTime StartTime = DateTime.UtcNow;
            while ((DateTime.UtcNow - StartTime).TotalSeconds <= seconds)
            {
                WaitForPause(pauseToken);
                InvokeCurrentTaskProgressChanged(((DateTime.UtcNow - StartTime).TotalSeconds / seconds) * 100);
                Thread.Sleep(20);
            }
        }
        public bool RemoveDriveStep()
        {
            Countdown("Imaging complete - Rebooting", "Rebooting in 5 seconds", 5);
            Process formatProcess = new Process();
            formatProcess.StartInfo.FileName = "wpeutil";
            formatProcess.StartInfo.UseShellExecute = false;
            formatProcess.StartInfo.RedirectStandardOutput = true;
            formatProcess.StartInfo.CreateNoWindow = true;
            formatProcess.StartInfo.Arguments = "reboot";
            formatProcess.Start();
            formatProcess.WaitForExit();
            Environment.Exit(0);
            return true;
        }
        

        private void UsbEventWatcher_UsbDeviceRemoved(object? sender, UsbDevice e)
        {
            InvokeCurrentTaskNameChanged("Imaging complete - Rebooting");
            InvokeCurrentTaskMessageChanged("Flash drive removed, rebooting");
            DriveRemoved = true;
        }

        private double GetProgressPercent(int maxSteps, int step)
        {
            return ( (double)step / (double)maxSteps ) * 100;
        }

        private bool TakeHome = false;
        public void ApplyTakeHome(bool Enabled)
        {
            if (Enabled)
            {
                TakeHome = true;
            }
        }

        private void WaitForPause(PauseToken pauseToken) {
            if (!pauseToken.IsPaused) return;
            InvokeCurrentTaskNameChanged("Paused");
            InvokeCurrentTaskMessageChanged("Waiting for unpause");
            InvokeCurrentTaskProgressChanged(0, true);
            pauseToken.WaitWhilePaused();
        }
        private UserDataContext _context;

        private List<StepBase> Steps = new List<StepBase>()
        {
            new FormatStep(),
            new ApplyImageStep(),
            new DisableTakeHomeStep(),
            new ApplyDellBiosSettingsStep(),
            new ApplyAutopilotConfigurationStep(),
            new ApplyWifiStep(),
            new MakeDiskBootableStep(),
            new RemoveUnattendXMLStep(),
            new RebootStep()
        };

        private int CurrentStep = 1;

        public void Run(UserDataContext context, PauseToken pauseToken)
        {
            pauseToken.WaitWhilePaused();
            _context = context;
            try
            {
                foreach (var step in Steps)
                {
                    InvokeCurrentTaskMessageChanged("");
                    InvokeCurrentTaskNameChanged("");
                    InvokeCurrentTaskProgressChanged(0, false);

                    step.StepUpdated += StepOnStepUpdated;
                    var result = step.Run(context, pauseToken).ConfigureAwait(true).GetAwaiter().GetResult();
                    if (result.Success)
                    {
                        InvokeCurrentTaskMessageChanged(result.Message);
                        Thread.Sleep(500);
                    }
                    else
                    {
                        if (!step.Critical)
                        {
                            InvokeCurrentTaskNameChanged("Failed");
                            InvokeCurrentTaskMessageChanged(result.Message);
                            Thread.Sleep(10000);
                        }
                        else
                        {
                            InvokeCurrentTaskNameChanged("Failed - Cannot continue");
                            InvokeCurrentTaskMessageChanged(result.Message);
                            break;

                        }
                    }

                    step.StepUpdated -= StepOnStepUpdated;
                }
            }
            catch (Exception e)
            {
                Logger.Error(e);
            }
            


            if (!Enabled) {
                WimMan.getInstance().Preload();
            }


            
        }

        private void StepOnStepUpdated(object? sender, StepBase.StepStatus e)
        {
            double totalProgress = Steps.Average(x => x.Progress);
            Debug.WriteLine($"Step: {CurrentStep}, Progress: {e.Progress}, Total: {totalProgress}");
            InvokeTotalTaskProgressChanged(totalProgress);
            InvokeCurrentTaskMessageChanged(e.Message);
            InvokeCurrentTaskNameChanged(e.Title);
            InvokeCurrentTaskProgressChanged(e.Progress, e.IsIndeterminate);
        }

        public void TaskManager_InternetBecameAvailable(object? sender, EventArgs e) {
            if (!UpdatedImageAvailable)
            {
                UpdatedImageAvailable = !wimCache.IsUpToDate;
            }
            
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
