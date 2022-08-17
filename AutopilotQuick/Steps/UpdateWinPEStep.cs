using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Humanizer;
using Nito.AsyncEx;
using NLog;

namespace AutopilotQuick.Steps;

public class UpdateWinPEStep : StepBaseEx
{
    public readonly Logger Logger = LogManager.GetCurrentClassLogger();
    private Cacher WinPEISOCache;

    public new bool Critical = false;

    
    public async Task<string> MountWinPEISO()
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
        Logger.Info($"MountWinPEOutput: {output}");
        return output.Trim();
    }

    public string FindEnvironmentDisk()
    {
        var EnvironmentDrive = Microsoft.Win32.Registry.GetValue("HKEY_LOCAL_MACHINE\\System\\CurrentControlSet\\Control", "PEBootRamdiskSourceDrive", null);
        if (EnvironmentDrive is not null)
        {
            var EDriveStr = Convert.ToString(EnvironmentDrive);
            Logger.Info($"Identified Environment drive as {EDriveStr}");
            if (EDriveStr is not null)
            {
                return EDriveStr;
            }
            Logger.Info($"Failed to convert Drive to String");
            return "";
        }
        Logger.Info($"Failed to find environment drive.");
        return "";
    }
    
    public override async Task<StepResult> Run(UserDataContext context, PauseToken pauseToken)
    {
        WinPEISOCache = new Cacher(
            "http://nettools.psd202.org/AutoPilotFast/OSDCloud_NoPrompt.iso", 
            "OSDImage.iso", context);
        
        var startTime = Stopwatch.StartNew();
        if (!IsEnabled || (!InternetMan.getInstance().IsConnected && !InternetMan.CheckForInternetConnection()))
        {
            Title = "Updating environment - DISABLED";
            Message = "Will continue after 5 seconds";
            await Task.Run(() => CountDown(pauseToken, 5000));
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
            Logger.Info("Downloading updated WinPEISO");
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
            Logger.Error("WinPEISO is not downloaded, this means something went wrong.");
            return new StepResult(false, "Failed to update WinPE, File does not exist.");
        }

        IsIndeterminate = true;
        Message = "Mounting WinPE ISO";
        var DriveOfEnvironmentISO = await MountWinPEISO();
        Logger.Info($"Environment ISO mounted to {DriveOfEnvironmentISO}:\\");

        Message = "Finding Environment Disk";
        var EnvironmentDrive = FindEnvironmentDisk();
        Logger.Info($"Environment drive has drive letter: {EnvironmentDrive}");
        Logger.Info($"Identification took {startTime.Elapsed.Humanize(3)}.");
        Message = "Attempting update...";
        Logger.Info("Starting to robocopy");

        Title = "Updating Flash Drive OS";
        var output = await InvokePowershellScriptAndGetResultAsync(
            $"robocopy {DriveOfEnvironmentISO}:\\ '{EnvironmentDrive}' *.* /e /ndl /njh /njs /np /r:0 /w:0 /b /zb", CancellationToken.None);
        Logger.Info($"Robocopy Output: {output}");
        Message = "Updated flash drive successfully";
        Progress = 100;
        Logger.Info($"Update step took {startTime.Elapsed.Humanize(3)}");
        
        //Restore the last-modified parameter so we do not re-download and update
        WinPEISOCache.SetCachedFileLastModified(SavedLastModified);
        return new StepResult(true, $"Applied update in {startTime.Elapsed.Humanize(3)}");
    }
}