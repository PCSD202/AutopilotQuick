using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AutopilotQuick.WMI;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Nito.AsyncEx;
using ORMi;
using Polly;
using Polly.Retry;

namespace AutopilotQuick.Steps
{
    internal class FormatStep : StepBaseEx
    {
        public override string Name() => "Format disk step";
        public readonly ILogger Logger = App.GetLogger<FormatStep>();
        public int IdentifyDriveToImage()
        {
            using (var t = App.telemetryClient.StartOperation<RequestTelemetry>("Identifying drive to format"))
            {
                Message = "Identifying drive...";
                IsIndeterminate = true;
                try
                {
                    WMIHelper helper = new WMIHelper("root\\CimV2");
                    DiskDrive diskToSelect = helper.Query<DiskDrive>().First(x => x.InterfaceType != "USB" && x.MediaLoaded);
                    t.Telemetry.Properties["Drive"] = JsonConvert.SerializeObject(diskToSelect);
                    Logger.LogInformation("Identified drive {@drive} to format", diskToSelect);
                    App.telemetryClient.TrackEvent("DriveToFormatIdentified", new Dictionary<string, string>()
                    {
                        {"Drive", JsonConvert.SerializeObject(diskToSelect)}
                    });
                    return (int)diskToSelect.Index;
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Got exception while identifying drive to image");
                    App.GetTelemetryClient().TrackException(ex);
                    return -1;
                }      
            }
        }

        public bool FormatDrive(int DriveToImage)
        {
            using (App.GetTelemetryClient().StartOperation<RequestTelemetry>("Formatting drive"))
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
                    Logger.LogInformation(
                        $"Diskpart output: {Regex.Replace(diskpartOutput, @"^\s*$\n|\r", string.Empty, RegexOptions.Multiline).TrimEnd()}");

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
                    Logger.LogError(ex, "Got error while trying to format disk");
                    Message = $"Failed to format disk, check logs.";
                    Progress = 100;
                    IsIndeterminate = false;
                    return false;
                }
            }
        }

        public override async Task<StepResult> Run(UserDataContext context, PauseToken pauseToken, IOperationHolder<RequestTelemetry> StepOperation)
        {
            if (IsEnabled)
            {
                Title = "Formatting drive";
                Progress = 0;
                IsIndeterminate = false;
                int DriveToImage = -1;
                try
                {
                    DriveToImage = IdentifyDriveToImage();
                    StepOperation.Telemetry.Properties["DriveNumber"] = $"{DriveToImage}";
                    if (DriveToImage == -1)
                    {
                        throw new DriveNotFoundException();
                    }
                }
                catch (DriveNotFoundException)
                {
                    return new StepResult(false, "Failed to identify a drive to format. This could be because of a bad hard drive, or not having one installed.");
                }
                

                Progress = 50;
                try
                {
                    var result = FormatDrive(DriveToImage);
                    if (!result)
                    {
                        throw new DriveNotFoundException();
                    }
                }
                catch (DriveNotFoundException)
                {
                    return new StepResult(false, "Failed to format drive. This could be because of a bad hard drive, or not having one installed.");
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
