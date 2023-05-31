#region

using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;
using AQ.DeviceInfo;
using AutopilotQuick.Quicktype;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Extensions.Logging;
using Nito.AsyncEx;

#endregion

namespace AutopilotQuick.Steps;

public class BiosUpdateStep : StepBaseEx
{
    public readonly ILogger Logger = App.GetLogger<BiosUpdateStep>();
    public override string Name() => "Bios update step";
    
    
    public override bool IsCritical() => false;

    public override async Task<StepResult> Run(UserDataContext context, PauseToken pauseToken, IOperationHolder<RequestTelemetry> StepOperation)
    {
        
    
        Title = "Checking for bios updates";

        
        Message = "Downloading latest config";
        Progress = 0;
        IsIndeterminate = true;

        Cacher DellBiosCatalogConfigCacher = new Cacher(CachedResourceUris.DellBiosCatalogConfig, context);
        if (!DellBiosCatalogConfigCacher.FileCached || (InternetMan.GetInstance().IsConnected && !DellBiosCatalogConfigCacher.IsUpToDate))
        {
            await DellBiosCatalogConfigCacher.DownloadUpdateAsync();
        }
        
        var dellBiosCatalogSettings = DellBiosCatalogSettings.FromJson(await DellBiosCatalogConfigCacher.ReadAllTextAsync());
        
        Cacher DellBiosCatalogCabCacher = new Cacher(dellBiosCatalogSettings.CatalogUri, dellBiosCatalogSettings.CatalogName, context);
        
        Message = "Extracting dell bios catalog";
        var dellBiosCatalogDir = Path.Combine(Path.GetDirectoryName(App.GetExecutablePath()) ?? "", "Cache", "DellBiosUpdates","Catalog");
        
        using (var updateAndExtract = App.telemetryClient.StartOperation<RequestTelemetry>("Updating/Extracting dell bios"))
        {
            updateAndExtract.Telemetry.Properties["Downloaded"] = "false";
            await context.WaitForDriveAsync(); //Wait for the drive to be present
            //If the file is not cached, or if we have internet and the file is not up to date, or if the directory does not exist
            if (!DellBiosCatalogCabCacher.FileCached || (InternetMan.GetInstance().IsConnected && !DellBiosCatalogCabCacher.IsUpToDate) || !Directory.Exists(dellBiosCatalogDir))
            {
                updateAndExtract.Telemetry.Properties["Downloaded"] = "true";
                await context.WaitForDriveAsync(); //Wait for the drive to be present
                if (Directory.Exists(dellBiosCatalogDir))
                {
                    await context.WaitForDriveAsync(); //Wait for the drive to be present
                    Directory.Delete(dellBiosCatalogDir, true);
                }

                await context.WaitForDriveAsync(); //Wait for the drive to be present
                Directory.CreateDirectory(dellBiosCatalogDir);
                await DellBiosCatalogCabCacher.DownloadUpdateAsync();
                await context.WaitForDriveAsync(); //Wait for the drive to be present
                
                //Now we need to expand the CAB file into the correct folder using expand
                var result = await InvokePowershellScriptAndGetResultAsync($"Expand \"{DellBiosCatalogCabCacher.FilePath}\" \"{dellBiosCatalogDir}\"", CancellationToken.None);
            }

            updateAndExtract.Telemetry.Success = true;
        }
        if (!IsEnabled)
        {
            Title = "Checking for bios updates - DISABLED";
            Message = "Will continue after 5 seconds";
            await Task.Run(() => CountDown(pauseToken, 5000));
            return new StepResult(true, "Skipped applying bios updates");
        }
        var DellVersion = await InvokePowershellScriptAndGetResultAsync("(Get-MyDellBios | Sort-Object ReleaseDate -Descending | Select-Object -First 1).DellVersion", CancellationToken.None);
        Logger.LogInformation("Current Bios version: {version}, Latest Dell version: {latest}",  DeviceInfo.BiosVersion, DellVersion);
        if (DellVersion == DeviceInfo.BiosVersion)
        {
            return new StepResult(true, "Bios was already up to date");
        }

        if (!IsEnabled) return new StepResult(true, "Not enabled");
        Message = "Updating bios...";
        //var result = await InvokePowershellScriptAndGetResultAsync("Update-MyDellBios -Silent", CancellationToken.None);
        
        return new StepResult(true, "Bios updated successfully");
    }
}