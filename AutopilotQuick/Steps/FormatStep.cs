using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Nito.AsyncEx;
using NLog;

namespace AutopilotQuick.Steps
{
    internal class FormatStep : StepBaseEx
    {
        public readonly Logger Logger = LogManager.GetCurrentClassLogger();
        public int IdentifyDriveToImage()
        {
            Message = "Identifying drive to image...";
            IsIndeterminate = true;
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
                    Message = $"Identified drive {intDiskNum}";
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
                Message = $"Identified drive {DriveToImage}, running diskpart";
                var diskpartOutput = RunDiskpartScript(diskpartScript);
                Logger.Debug($"Diskpart output: {Regex.Replace(diskpartOutput, @"^\s*$\n|\r", string.Empty, RegexOptions.Multiline).TrimEnd()}");

                if (diskpartOutput != null && diskpartOutput.Split(" ")
                        .Count(x => x.ToLower() == "successfully" || x.ToLower() == "succeeded") >= 14)
                {
                    Message = $"Successfully formated drive {DriveToImage}";
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                Logger.Error(ex);
                Message = $"Failed to format disk, check logs.";
                Progress = 100;
                IsIndeterminate = false;
                return false;
            }
        }

        public override async Task<StepResult> Run(UserDataContext context, PauseToken pauseToken)
        {
            if (IsEnabled)
            {
                Title = "Formatting drive";
                Progress = 0;
                IsIndeterminate = false;
                int DriveToImage = IdentifyDriveToImage();
                if (DriveToImage == -1)
                {
                    return new StepResult(false, "Failed to identify a drive to image. Maybe none installed?");
                }

                Progress = 50;
                var result = FormatDrive(DriveToImage);
                if (!result)
                {
                    return new StepResult(false, "Failed to format drive");
                }
            }
            else
            {
                Title = "Formatting drives - DISABLED";
                Message = "Will continue after 5 seconds";
                await Task.Run(() => CountDown(pauseToken, 5000));
            }
            Progress = 100;
            return new StepResult(true, "Successfully formatted drive");
        }
    }
}
