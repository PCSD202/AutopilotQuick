﻿using System;
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
using Microsoft.Wim;
using System.Reflection;

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
        public static WimCacher wimCache;
        public bool UpdatedImageAvailable = false;
       

        public bool Enabled = false;


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
                Logger.Debug($"Diskpart output: {diskpartOutput}");
                diskpartProcess.WaitForExit();
                File.Delete(diskpartScriptPath);
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
                    if (!File.Exists(wimCache.WimPath) || UpdatedImageAvailable)
                    {
                        UpdatedImageAvailable = false;
                        wimCache.DownloadUpdatedISO().ConfigureAwait(false).GetAwaiter().GetResult();
                    }

                    using (var wimHandle = WimgApi.CreateFile(wimCache.WimPath, WimFileAccess.Read, WimCreationDisposition.OpenExisting, WimCreateFileOptions.None, WimCompressionType.None))
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
            if (UpdatedImageAvailable)
            {
                UpdatedImageAvailable = false;
                wimCache.DownloadUpdatedISO().ConfigureAwait(false).GetAwaiter().GetResult();
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

                    InvokeCurrentTaskMessageChanged($"Applying windows image {progressMessage.PercentComplete}%");
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
                var WMIOutput = InvokePowershellScriptAndGetResult(@"Get-WmiObject -Query ""SELECT * FROM Win32_ComputerSystem WHERE Model LIKE '%Optiplex%'""");
                Logger.Debug($"WMI output: {WMIOutput}");
                if (WMIOutput != "")
                {
                    scriptExecutable = "DesktopBiosSettings.cmd";
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

        private double GetProgressPercent(int maxSteps, int step)
        {
            return ( (double)step / (double)maxSteps ) * 100;
        }

        public void Run(UserDataContext context)
        {
            wimCache = new WimCacher("http://sccm2.psd202.org/WIM/21H2-install.wim", context);
            InternetMan.getInstance().InternetBecameAvailable += TaskManager_InternetBecameAvailable;
            var maxSteps = 7;
            bool success = ApplyDellBiosSettings();
            if (!success)
            {
                InvokeCurrentTaskNameChanged("Failed to apply dell bios settings");
                InvokeTotalTaskProgressChanged(100, false);
            }
            InvokeTotalTaskProgressChanged(GetProgressPercent(maxSteps, 1), false);

            success = FormatStep();
            if (!success)
            {
                InvokeCurrentTaskNameChanged("Failed to image drive");
                InvokeTotalTaskProgressChanged(100, false);
                Thread.Sleep(10000);
            }
            InvokeTotalTaskProgressChanged(GetProgressPercent(maxSteps, 2), false);

            success = ApplyImageStep();
            if (!success)
            {
                InvokeCurrentTaskNameChanged("Failed to apply image to drive");
                InvokeTotalTaskProgressChanged(100, false);
            }
            InvokeTotalTaskProgressChanged(GetProgressPercent(maxSteps, 3), false);

            success = ApplyWindowsAutopilotConfigurationStep();
            if (!success)
            {
                InvokeCurrentTaskNameChanged("Failed to apply autopilot configuration file");
                InvokeTotalTaskProgressChanged(100, false);
            }
            InvokeTotalTaskProgressChanged(GetProgressPercent(maxSteps, 4), false);

            success = ApplyWifiStep();
            if (!success)
            {
                InvokeCurrentTaskNameChanged("Failed to apply wifi settings");
                InvokeTotalTaskProgressChanged(100, false);
            }
            InvokeTotalTaskProgressChanged(GetProgressPercent(maxSteps, 5), false);

            success = MakeDiskBootable();
            if (!success)
            {
                InvokeCurrentTaskNameChanged("Failed to make disk bootable");
                InvokeTotalTaskProgressChanged(100, false);
            }
            InvokeTotalTaskProgressChanged(GetProgressPercent(maxSteps, 6), false);

            success = RemoveUnattendXMLStep();
            if (!success)
            {
                InvokeCurrentTaskNameChanged("Failed to delete unattend");
                InvokeTotalTaskProgressChanged(100, false);
            }
            InvokeTotalTaskProgressChanged(GetProgressPercent(maxSteps, 7), false);

            InvokeCurrentTaskNameChanged("Finished");

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