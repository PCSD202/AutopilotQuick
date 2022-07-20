using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using Humanizer;
using Nito.AsyncEx;
using NLog;

namespace AutopilotQuick.Steps;

public class InitializePowershellStep : StepBaseEx
{
    public readonly Logger Logger = LogManager.GetCurrentClassLogger();
    public new bool Critical = false;
    public override async Task<StepResult> Run(UserDataContext context, PauseToken pauseToken)
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
            Logger.Error($"Failed to initialize powershell. Output: {output}, Expected: 'Started' ");
            return new StepResult(false, "Powershell failed to initialize.");
        }


    }
}