using System.Threading;
using System.Threading.Tasks;
using Nito.AsyncEx;
using NLog;

namespace AutopilotQuick.Steps;

public class ApplyProductKeyStep : StepBaseEx
{
    public readonly Logger Logger = LogManager.GetCurrentClassLogger();
    public override async Task<StepResult> Run(UserDataContext context, PauseToken pauseToken)
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
        Logger.Debug("Dism Script output: "+output);
        Progress = 100;
        return new StepResult(true, "Applied windows product key");
    }
}