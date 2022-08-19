using System.Threading;
using System.Threading.Tasks;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Extensions.Logging;
using Nito.AsyncEx;

namespace AutopilotQuick.Steps;

public class ApplyProductKeyStep : StepBaseEx
{
    public override string Name() => "Apply ProductKey step";
    public readonly ILogger Logger = App.GetLogger<ApplyProductKeyStep>();
    public override async Task<StepResult> Run(UserDataContext context, PauseToken pauseToken,
        IOperationHolder<RequestTelemetry> StepOperation)
    {
        if (!context.TakeHomeToggleOn)
        {
            Progress = 100;
            Title = "Applying product key - DISABLED";
            return new StepResult(true, "Finished applying product key");
        }
        Title = "Applying Windows product key";
        IsIndeterminate = true;
        Message = "Running dism...";
        var output = await InvokePowershellScriptAndGetResultAsync("dism.exe /Image=W:\\ /Set-ProductKey:8PTT6-RNW4C-6V7J2-C2D3X-MHBPB", CancellationToken.None);
        Logger.LogDebug("Dism Script output: {output}",output);
        Progress = 100;
        return new StepResult(true, "Applied windows product key");
    }
}