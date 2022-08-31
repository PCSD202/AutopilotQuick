using System;
using System.Collections.Generic;
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
using Polly;
using Polly.Retry;
using RestSharp;

namespace AutopilotQuick.Steps;

public class CleanupRecordsStep : StepBaseEx
{
    public readonly ILogger Logger = App.GetLogger<CleanupRecordsStep>();
    
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


    public class TakeHomeInfo
    {
        public string ApiKey { get; set; }
        public string Url { get; set; }
    }
    
    public async Task<TakeHomeInfo> GetAPIInfo(UserDataContext context)
    {
        var takehomeCredsCacher = new Cacher(
            "https://nettools.psd202.org/AutoPilotFast/Takehome.json",
            "TakeHome.json", context);
        if (!takehomeCredsCacher.FileCached || !takehomeCredsCacher.IsUpToDate)
        {
            await takehomeCredsCacher.DownloadUpdateAsync();
        }

        try
        {
            var info = JsonConvert.DeserializeObject<TakeHomeInfo>(await File.ReadAllTextAsync(takehomeCredsCacher.FilePath));
            if (info is not null) return info;
            takehomeCredsCacher.Delete(); //Force it to redownload
            return await GetAPIInfo(context);
        }
        catch (JsonException e)
        {
            takehomeCredsCacher.Delete(); //Force it to redownload
            return await GetAPIInfo(context);
        }
        
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
        IsIndeterminate = false;
        
        Message = "Looking up service tag...";
        var serviceTag = GetServiceTag(pauseToken);
        Progress = 25;
        
        Message = "Loading credentials";
        var APIData = await GetAPIInfo(context);
        Progress = 50;

        var client = new RestClient(APIData.Url);
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
        t.Telemetry.Url = new Uri(APIData.Url);
        try
        {
            var request = new RestRequest().AddHeader("x-functions-key", APIData.ApiKey).AddJsonBody(data);
            await InternetMan.WaitForInternetAsync(context); //Make sure we have internet before we post
            var response = await client.PostAsync(request);
            if (response.IsSuccessful)
            {
                t.Telemetry.Properties["ResultData"] = JsonConvert.SerializeObject(response, Formatting.Indented);
                t.Telemetry.Success = true;
                return new StepResult(true, "Cleaned up records");
            }
            t.Telemetry.Success = false;
            App.GetTelemetryClient().TrackEvent("CleanupError", new Dictionary<string, string>(){{"ServiceTag", GetServiceTag(pauseToken)}});
            return new StepResult(false, "Failed to process cleanup with azure");

        }
        catch
        {
            t.Telemetry.Success = false;
            App.GetTelemetryClient().TrackEvent("CleanupError", new Dictionary<string, string>(){{"ServiceTag", GetServiceTag(pauseToken)}});
            return new StepResult(false, "Failed to clean up records");
        }
    }
}