using System.Threading;
using System.Threading.Tasks;
using AQ.DeviceInfo;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Extensions.Logging;
using Nito.AsyncEx;

namespace AutopilotQuick.Steps;

public class BiosUpdateStep : StepBaseEx
{
    public readonly ILogger Logger = App.GetLogger<BiosUpdateStep>();
    public override string Name() => "Bios update step";
    public override bool IsCritical() => false;

    public override async Task<StepResult> Run(UserDataContext context, PauseToken pauseToken, IOperationHolder<RequestTelemetry> StepOperation)
    {
        Title = "Checking for bios updates";
        Message = "Getting current version";
        IsIndeterminate = true;
        if (!InternetMan.GetInstance().IsConnected)
        {
            Message = "Not checking for updates because I have no internet";
            return new StepResult(true, "Not checking for updates because I have no internet");
        }

        var DellVersion = await InvokePowershellScriptAndGetResultAsync("(Get-MyDellBios | Sort-Object ReleaseDate -Descending | Select-Object -First 1).DellVersion", CancellationToken.None);
        Logger.LogInformation("Current Bios version: {version}, Latest Dell version: {latest}",  DeviceInfo.BiosVersion, DellVersion);
        if (DellVersion == DeviceInfo.BiosVersion)
        {
            return new StepResult(true, "Bios was already up to date");
        }

        if (!IsEnabled) return new StepResult(true, "Not enabled");
        Message = "Updating bios...";
        var result = await InvokePowershellScriptAndGetResultAsync("Update-MyDellBios -Silent", CancellationToken.None);
        
        return new StepResult(true, "Bios updated successfully");
    }
}