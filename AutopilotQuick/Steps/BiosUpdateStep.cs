#region

using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
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
        
        Message = "Downloading latest catalog";
        Progress = 0;
        IsIndeterminate = true;

        Cacher DellBiosCatalogCacher = new Cacher(CachedResourceUris.DellBiosCatalog, context);
        if (!DellBiosCatalogCacher.FileCached || (InternetMan.GetInstance().IsConnected && !DellBiosCatalogCacher.IsUpToDate))
        {
            await DellBiosCatalogCacher.DownloadUpdateAsync();
        }
        var dellBiosCatalog = DellBiosCatalog.DellBiosCatalog.FromJson(await DellBiosCatalogCacher.ReadAllTextAsync());

        Message = "Downloading latest Flash64W";
        //Download and maintain Flash64W
        var Flash64WCacher = new Cacher(CachedResourceUris.Flash64W, context);
        if (!Flash64WCacher.FileCached || (InternetMan.GetInstance().IsConnected && !Flash64WCacher.IsUpToDate))
        {
            await Flash64WCacher.DownloadUpdateAsync();
        }

        var systemSKU = DeviceInfo.SystemSKUNumber;
        //Find the bios update for this machine
        Message = "Finding latest bios update in catalog";
        var supportedCatalogs = dellBiosCatalog.Where(x => x.SupportedSystemId.Value.Contains(systemSKU)).OrderByDescending(x => x.ReleaseDate).ToList();
        if (!supportedCatalogs.Any())
        {
            //Bios update not found in catalog
            return new StepResult(true, "No updates found");
        }
        
        //Compare bios versions
        var latestUpdate = supportedCatalogs.First();
        var latestUpdateVersion = new Version(latestUpdate.DellVersion);
        var myVersion = new Version(DeviceInfo.BiosVersion);
        if (latestUpdateVersion <= myVersion)
        {
            //return new StepResult(true, $"No updates needed, already on latest version {latestUpdate.DellVersion}");
        }

        var latestBiosCacher = new Cacher(latestUpdate.Url, latestUpdate.FileName, context);
        
        Message = "Downloading latest bios";
        //Download and maintain latest bios update
        if (!latestBiosCacher.FileCached || (InternetMan.GetInstance().IsConnected && !latestBiosCacher.IsUpToDate))
        {
            await latestBiosCacher.DownloadUpdateAsync();
        }
        
        if (!IsEnabled)
        {
            Title = "Updating bios - DISABLED";
            Message = "Will continue after 5 seconds";
            await Task.Run(() => CountDown(pauseToken, 5000));
            return new StepResult(true, "Skipped applying bios updates");
        }
        
        Message = "Updating bios...";

        var output = await SecretProvisioningPackage.ApplyAsync(this, SecretNames.BiosUpdatePackage);
        Logger.LogDebug("Apply bios package step: {output}", output);

        var biosLocation = Path.Join("W:\\", "BiosUpdate");
        if (Directory.Exists(biosLocation))
        {
            Directory.Delete(biosLocation, true);
        }

        Directory.CreateDirectory(biosLocation);
        File.Copy(Flash64WCacher.FilePath, Path.Join(biosLocation, "Flash64W.exe"));
        File.Copy(latestBiosCacher.FilePath, Path.Join(biosLocation, latestBiosCacher.FileName));
        
        return new StepResult(true, "Bios updated successfully");
    }
}
