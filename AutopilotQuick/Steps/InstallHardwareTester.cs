#region

using System.Threading.Tasks;
using Humanizer;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using Nito.AsyncEx;

#endregion

namespace AutopilotQuick.Steps;

public class InstallHardwareTester : StepBaseEx
{
    public override bool IsCritical()
    {
        return false;
    }

    public override string Name()
    {
        return "Install hardware tester";
    }

    public override async Task<StepResult> Run(UserDataContext context, PauseToken pauseToken, IOperationHolder<RequestTelemetry> StepOperation)
    {
        Title = "Installing Hardware tester";
        if (!IsEnabled)
        {
            Title = "Installing Hardware tester - DISABLED";
            Message = "Will continue after 1 second";
            await Task.Run(async () => await CountDown(pauseToken, 1000));
            return new StepResult(true, "Skipped installing HWT.exe because not enabled");
        }
        Message = "Checking for hardware tester updates";
        var hwtCacher = new Cacher(CachedResourceUris.HardwareTester, context);
        if (!hwtCacher.FileCached ||
            (InternetMan.GetInstance().IsConnected && !hwtCacher.IsUpToDate))
        {
            await hwtCacher.DownloadUpdateAsync();
        }

        Message = "Copying hardware tester...";
        var dest = @"W:\Windows\System32\hwt.exe";
        var source = hwtCacher.FilePath;
        var fileCopier = new CustomFileCopier(source, dest);
        fileCopier.OnProgressChanged += (long size, long downloaded, double percentage, ref bool cancel) =>
        {
            Progress = percentage;
            Message = $"Copying hardware tester... {percentage/100:P1} ({downloaded.Bytes().Humanize()} / {size.Bytes().Humanize()})";
        };
        await context.WaitForDriveAsync(); //Wait for the drive to be present
        await fileCopier.CopyAsync();
        return new StepResult(true, "Successfully installed the hardware tester");

    }
}