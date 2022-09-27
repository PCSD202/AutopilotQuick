using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Nito.AsyncEx;
using RestSharp;

namespace AutopilotQuick.Steps;

public class LogTakeHomeStep : StepBaseEx
{
    public override string Name()
    {
        return "Log take home step";
    }

    public class TakeHomeLaptopRequest
    {
        public string ServiceTag { get; set; }
        public string DeviceID { get; set; }
    }

    public class TakeHomeLaptopLoggerInfo
    {
        public string ApiKey { get; set; }
        public string Url { get; set; }
    }
    
    public readonly ILogger Logger = App.GetLogger<LogTakeHomeStep>();
    
    public override async Task<StepResult> Run(UserDataContext context, PauseToken pauseToken, IOperationHolder<RequestTelemetry> StepOperation)
    {
        Title = "Logging take home";
        if (!context.TakeHomeToggleOn)
        {
            return new StepResult(true, "Skipped logging take home because take home is off");
        }
        Message = "Loading credentials";
        IsIndeterminate = false;
        Progress = 0;
        var takehomeCredsCacher = new Cacher(CachedResourceUris.TakeHomeLoggerConfig, context);
        if (!takehomeCredsCacher.FileCached || !takehomeCredsCacher.IsUpToDate)
        {
            await takehomeCredsCacher.DownloadUpdateAsync();
        }

        var logInfo =
            JsonConvert.DeserializeObject<TakeHomeLaptopLoggerInfo>(await File.ReadAllTextAsync(takehomeCredsCacher.FilePath));

        Progress = 50;
        var thingToSend = new TakeHomeLaptopRequest()
        {
            DeviceID = DeviceID.DeviceIdentifierMan.getInstance().GetDeviceIdentifier(),
            ServiceTag = GetServiceTag(pauseToken)
        };
        Message = "Sending message";
        using var t =App.GetTelemetryClient().StartOperation<RequestTelemetry>("Logging to take home function");
        t.Telemetry.Url = new Uri(logInfo.Url);
        var client = new RestClient(logInfo.Url);
        var request = new RestRequest().AddHeader("x-functions-key", logInfo.ApiKey).AddJsonBody(thingToSend);
        await InternetMan.WaitForInternetAsync(context);
        var response = await client.PostAsync(request);
        if (response.IsSuccessful)
        {
            App.GetTelemetryClient().TrackEvent("TakeHome", new Dictionary<string, string>(){{"ServiceTag", GetServiceTag(pauseToken)}});
            return new StepResult(true, "Successfully logged take home with azure");
        }
        App.GetTelemetryClient().TrackEvent("TakeHomeError", new Dictionary<string, string>(){{"ServiceTag", GetServiceTag(pauseToken)}});
        return new StepResult(false, "Failed to log take home with azure");
    }
}