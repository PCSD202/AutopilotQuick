using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.ApplicationInsights.Extensibility.Implementation;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Nito.AsyncEx;
using RestSharp;

namespace AutopilotQuick.Steps;

public class CleanupRecordsStep : StepBaseEx
{
    public readonly ILogger Logger = App.GetLogger<FinalizeSyncingLogsStep>();
    
    public override string Name() => "Cleanup records step";
    
    public class PostData
    {
        public string ServiceTagToAudit { get;set; }
        public string DeviceID { get;set; }
        public bool DeleteAutopilot { get;set; }
        public bool DeleteIntune { get;set; }
        public bool Dry { get; set; }
    }
    public record struct ResultStruct(
        bool AutopilotDeviceDeleted,
        bool IntuneDeviceDeleted,
        bool AutopilotDeviceFound,
        bool IntuneDeviceFound);


    public async Task<string> GetAPIKey(UserDataContext context)
    {
        var takehomeCredsCacher = new Cacher("http://nettools.psd202.org/AutoPilotFast/AQTakeHomeAPIKey.txt", "AQTakeHomeAPIKey.txt", context);
        if (!takehomeCredsCacher.FileCached || !takehomeCredsCacher.IsUpToDate)
        {
            await takehomeCredsCacher.DownloadUpdateAsync();
        }
        
        return (await File.ReadAllTextAsync(takehomeCredsCacher.FilePath)).Trim();
    }

    public override async Task<StepResult> Run(UserDataContext context, PauseToken pauseToken, IOperationHolder<RequestTelemetry> StepOperation)
    {
        if (!IsEnabled)
        {
            Title = "Cleaning up intune records - DISABLED";
            await Task.Run(() => CountDown(pauseToken, 5000));
            return new StepResult(true, "Cleaning up records - DISABLED");
        }
        
        //if Take home enabled, wait for internet. If take home not enabled then we will not wait
        if (context.TakeHomeToggleOn & !InternetMan.getInstance().IsConnected)
        {
            await InternetMan.WaitForInternetAsync(context);
        }

        if (!InternetMan.getInstance().IsConnected && !context.TakeHomeToggleOn)
        {
            Title = "Cleaning up intune records - NO INTERNET";
            Progress = 100;
            await Task.Run(() => CountDown(pauseToken, 5000));
            return new StepResult(true, "Skipped cleaning up records due to not having internet");
        }
        
        Title = "Cleaning up records";
        Progress = 0;
        IsIndeterminate = true;
        
        Message = "Looking up service tag...";
        var serviceTag = GetServiceTag(pauseToken);

        var APIKey = await GetAPIKey(context);
        Message = "Getting API Key...";
        var APIURL = $"https://aqtakehome.azurewebsites.net/api/AQDeviceAudit?code={APIKey}";
        
        var client = new RestClient(APIURL);
        var data = new PostData()
        {
            DeleteAutopilot = context.TakeHomeToggleOn,
            DeleteIntune = true,
            DeviceID = DeviceID.DeviceIdentifierMan.getInstance().GetDeviceIdentifier(),
            Dry = false,
            ServiceTagToAudit = serviceTag
        };
        Message = "Executing cleanup...";
        using var t = App.GetTelemetryClient().StartOperation<RequestTelemetry>("Executing delete");
        t.Telemetry.Url = new Uri(APIURL);
        try
        {
            var response = await client.PostJsonAsync<PostData, ResultStruct>("", data, CancellationToken.None);
            t.Telemetry.Properties["ResultData"] = JsonConvert.SerializeObject(response, Formatting.Indented);
            t.Telemetry.Success = true;
            return new StepResult(true, "Cleaned up records");
        }
        catch
        {
            t.Telemetry.Success = false;
            return new StepResult(false, "Failed to clean up records");
        }
    }
}