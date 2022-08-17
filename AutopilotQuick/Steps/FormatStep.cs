using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AutopilotQuick.WMI;
using Newtonsoft.Json;
using Nito.AsyncEx;
using NLog;
using ORMi;
using Polly;
using Polly.Retry;

namespace AutopilotQuick.Steps
{
    internal class FormatStep : StepBaseEx
    {
        public readonly Logger Logger = LogManager.GetCurrentClassLogger();
        public int IdentifyDriveToImage()
        {
            Message = "Identifying drive to image...";
            IsIndeterminate = true;
            try
            {
                WMIHelper helper = new WMIHelper("root\\CimV2");
                DiskDrive diskToSelect = helper.Query<DiskDrive>().First(x => x.InterfaceType != "USB" && x.MediaLoaded);
                
                return (int)diskToSelect.Index;
            }
            catch (Exception ex)
            {
                Logger.Error(ex);
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

                
                RetryPolicy policy = RetryPolicy.Handle<DriveNotFoundException>()
                    .WaitAndRetry(5, i => TimeSpan.FromSeconds(5));
                var result = policy.ExecuteAndCapture(() =>
                {
                    Progress = 0;
                    int DriveToImage = IdentifyDriveToImage();
                    if (DriveToImage == -1)
                    {
                        throw new DriveNotFoundException();
                    }

                    Progress = 50;
                    var result = FormatDrive(DriveToImage);
                    if (!result)
                    {
                        throw new DriveNotFoundException();
                    }
                    return;
                });
                
                if (result.Outcome == OutcomeType.Failure)
                {
                    return new StepResult(false, "Failed to identify or format a drive to image. This could be because of a bad hard drive, or not having one installed.");
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
