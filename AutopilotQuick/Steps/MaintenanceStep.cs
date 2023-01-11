#region

using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Extensions.Logging;
using Nito.AsyncEx;

#endregion

namespace AutopilotQuick.Steps;

public class MaintenanceStep : StepBaseEx
{
    public override string Name() => "Maintenance Step";
    
    public override bool IsCritical()
    {
        return false;
    }

    public readonly ILogger Logger = App.GetLogger<MaintenanceStep>();

    private bool IsScript(string filename)
    {
        if (filename.StartsWith("script-") && filename.EndsWith(".ps1"))
        {
            return true;
        }

        if (filename.StartsWith("diskpart-") && filename.EndsWith(".txt"))
        {
            return true;
        }

        return false;
    }
    public override async Task<StepResult> Run(UserDataContext context, PauseToken pauseToken, IOperationHolder<RequestTelemetry> StepOperation)
    {
        Title = "AQ Maintenance";
        Message = "Running maintenance tasks (This should not take more than a couple seconds)";
        IsIndeterminate = true;
        var scriptFilesPath = Path.GetDirectoryName(App.GetRealExecutablePath());
        var AllFiles = Directory.GetFiles(scriptFilesPath);
        var scriptFiles = AllFiles.Where(x => IsScript(Path.GetFileName(x))).ToList();
        var deletedCount = 0;
        foreach (var scriptFile in scriptFiles)
        {
            try
            {
                File.Delete(scriptFile);
                deletedCount++;
            }
            catch (IOException e) { }
        }

        return new StepResult(true, $"Deleted {deletedCount} of {scriptFiles.Count} script files.");
    }
}