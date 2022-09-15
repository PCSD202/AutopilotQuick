using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Humanizer;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Extensions.Logging;
using Nito.AsyncEx;
using RoboSharp;

namespace AutopilotQuick.Steps;

public class UpdateWinPEStep : StepBaseEx
{
    public readonly ILogger Logger = App.GetLogger<UpdateWinPEStep>();
    private Cacher WinPEISOCache;

    public new bool Critical = false;

    public override string Name() => "Update WinPE step";

    public async Task<string> MountWinPEISO()
    {
        using (App.telemetryClient.StartOperation<RequestTelemetry>("Mounting WinPE ISO"))
        {
            var script = @$"$ISOFile = '{WinPEISOCache.FilePath}'";
            script = script + @"
#=================================================
Write-Verbose 'Getting Volumes ...'
#=================================================
$Volumes = (Get-Volume).Where({$_.DriveLetter}).DriveLetter

#=================================================
Write-Verbose 'Mounting the ISO ...'
#=================================================
Mount-DiskImage -ImagePath $ISOFile 1>$null

#=================================================
Write-Verbose 'Detemrining the Drive Letter of the Mounted ISO ...'
#=================================================
$ISO = (Compare-Object -ReferenceObject $Volumes -DifferenceObject (Get-Volume).Where({$_.DriveLetter}).DriveLetter).InputObject

Write-Host $ISO;
";
            var output = await InvokePowershellScriptAndGetResultAsync(script, CancellationToken.None);
            Logger.LogInformation("MountWinPEOutput: {output}", output);
            return output.Trim();
        }
    }

    public string FindEnvironmentDisk()
    {
        using (var t = App.telemetryClient.StartOperation<RequestTelemetry>("Finding environment disk"))
        {
            var EnvironmentDrive = Microsoft.Win32.Registry.GetValue("HKEY_LOCAL_MACHINE\\System\\CurrentControlSet\\Control", "PEBootRamdiskSourceDrive", null);
            if (EnvironmentDrive is not null)
            {
                var EDriveStr = Convert.ToString(EnvironmentDrive);
                Logger.LogInformation("Identified Environment drive as {drive}", EDriveStr);
                if (EDriveStr is not null)
                {
                    t.Telemetry.Success = true;
                    return EDriveStr;
                }

                t.Telemetry.Success = false;
                Logger.LogError($"Failed to convert Drive to String");
                return "";
            }

            t.Telemetry.Success = false;
            Logger.LogError($"Failed to find environment drive.");
            return "";
        }
    }
    
    public override async Task<StepResult> Run(UserDataContext context, PauseToken pauseToken,
        IOperationHolder<RequestTelemetry> StepOperation)
    {
        WinPEISOCache = new Cacher(CachedResourceUris.OsdImage, context);
        if (!IsEnabled || (!InternetMan.GetInstance().IsConnected && !InternetMan.CheckForInternetConnection()))
        {
            Title = "Updating environment - DISABLED";
            Message = "Will continue after 5 seconds";
            await Task.Run(async () => await CountDown(pauseToken, 5000));
            return new StepResult(true, "Skipped updating environment because no internet or not enabled");
        }

        Title = "Checking environment for updates";
        IsIndeterminate = true;

        Progress = 25;
        Message = "Making sure downloaded environment ISO is up to date";
        bool Updated = false;
        if (!WinPEISOCache.IsUpToDate)
        {
            Message = "Downloading updated environment ISO";
            Logger.LogInformation("Downloading updated WinPEISO");
            await WinPEISOCache.DownloadUpdateAsync();
            Updated = true;
            Message = "Downloaded updated environment ISO";
        }

        if (!Updated)
        {
            return new StepResult(true, "Skipped updating, no new OS file");
        }
        
        var SavedLastModified = WinPEISOCache.GetCachedFileLastModified(); //Temporarily reset the lastModified
        WinPEISOCache.SetCachedFileLastModified(DateTime.MinValue); //To trigger a re-download and update if we're interupted
        
        Progress = 50;
        Message = "Making sure the environment ISO is downloaded";
        if (!WinPEISOCache.FileCached)
        {
            Logger.LogError("WinPEISO is not downloaded, this means something went wrong.");
            return new StepResult(false, "Failed to update WinPE, File does not exist.");
        }

        IsIndeterminate = true;
        Message = "Mounting WinPE ISO";
        
        var DriveOfEnvironmentISO = await MountWinPEISO();
        Logger.LogInformation("Environment ISO mounted to {driveLetter}:\\", DriveOfEnvironmentISO);

        Message = "Finding Environment Disk";
        var EnvironmentDrive = FindEnvironmentDisk();
        Logger.LogInformation("Environment drive has drive letter: {driveLetter}", EnvironmentDrive);
        Message = "Attempting update...";
        Logger.LogInformation("Starting to robocopy");

        Title = "Updating Flash Drive OS";
        
        using (var t = App.telemetryClient.StartOperation<RequestTelemetry>("Robocopying OS to disk"))
        {
            
            CodePagesEncodingProvider.Instance.GetEncoding(437);
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            var command = new RoboCommand($"{DriveOfEnvironmentISO}:\\", $"{EnvironmentDrive}",
                CopyOptions.CopyActionFlags.Mirror, SelectionOptions.SelectionFlags.Default);
            command.OnCopyProgressChanged += (sender, args) =>
            {
                Message = $"Copying new OS. {args.CurrentFileProgress/100:P0} FILE: {args.CurrentFile.Name}";
                Progress = args.CurrentFileProgress;
            };
            await command.Start();
            
            Logger.LogInformation("Output: {@robocopyOutput}", command.GetResults());
            t.Telemetry.Success = command.GetResults().Status.Successful;
        }

        Message = "Updated flash drive successfully";
        Progress = 100;

        //Restore the last-modified parameter so we do not re-download and update
        WinPEISOCache.SetCachedFileLastModified(SavedLastModified);
        return new StepResult(true, $"Applied update");
    }
}