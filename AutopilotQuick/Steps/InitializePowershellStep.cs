using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using Humanizer;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Extensions.Logging;
using Nito.AsyncEx;

namespace AutopilotQuick.Steps;

public class InitializePowershellStep : StepBaseEx
{
    public override string Name() => "Initialize powershell step";
    public readonly ILogger Logger = App.GetLogger<FormatStep>();
    public override bool IsCritical()
    {
        return false;
    }
    public override async Task<StepResult> Run(UserDataContext context, PauseToken pauseToken,
        IOperationHolder<RequestTelemetry> StepOperation)
    {
        Title = "Initializing powershell";
        Message = "Starting powershell, please wait...";
        IsIndeterminate = true;
        var s = Stopwatch.StartNew();
        var output = InvokePowershellScriptAndGetResult("Write-Host Started");
        
        IsIndeterminate = false;
        Progress = 100;
        if (output == "Started")
        {
            return new StepResult(true, $"Powershell initialized successfully in {s.Elapsed.Humanize()}");
        }
        else
        {
            Logger.LogError("Failed to initialize powershell. Output: {output}, Expected: 'Started' ", output);
            return new StepResult(false, "Powershell failed to initialize.");
        }


    }
}