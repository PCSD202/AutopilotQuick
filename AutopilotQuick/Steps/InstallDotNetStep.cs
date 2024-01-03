using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using Humanizer;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using Nito.AsyncEx;

namespace AutopilotQuick.Steps;

public class InstallDotNetStep : StepBaseEx
{
    public override string Name()
    {
        return "Install Dotnet Step";
    }

    public override async Task<StepResult> Run(UserDataContext context, PauseToken pauseToken, IOperationHolder<RequestTelemetry> StepOperation)
    {
        Title = "Installing .NET 6 for Hardware Tester";
        if (!IsEnabled)
        {
            Title = "Installing .NET - DISABLED";
            Message = "Will continue after 5 seconds";
            await Task.Run(() => CountDown(pauseToken, 5000));
            return new StepResult(true, "Skipped installing .NET");
        }
        
        Message = "Extracting .NET install package";
        
        var installPackageDir = Path.Combine(Path.GetDirectoryName(App.GetExecutablePath()) ?? "", "Cache", "DotnetInstallPackage");
        Cacher installPackageCacher = new Cacher(CachedResourceUris.DotnetInstallPackage, context);
        
        using (var updateAndExtract = App.telemetryClient.StartOperation<RequestTelemetry>("Updating/Extracting dotnet installer"))
        {
            updateAndExtract.Telemetry.Properties["Downloaded"] = "false";
            await context.WaitForDriveAsync(); //Wait for the drive to be present
            //If the file is not cached, or if we have internet and the file is not up to date, or if the directory does not exist
            if (!installPackageCacher.FileCached ||
                (InternetMan.GetInstance().IsConnected && !installPackageCacher.IsUpToDate) ||
                !Directory.Exists(installPackageDir))
            {
                updateAndExtract.Telemetry.Properties["Downloaded"] = "true";
                await context.WaitForDriveAsync(); //Wait for the drive to be present
                if (Directory.Exists(installPackageDir))
                {
                    await context.WaitForDriveAsync(); //Wait for the drive to be present
                    Directory.Delete(installPackageDir, true);
                }

                await context.WaitForDriveAsync(); //Wait for the drive to be present
                Directory.CreateDirectory(installPackageDir);
                await installPackageCacher.DownloadUpdateAsync();
                await context.WaitForDriveAsync(); //Wait for the drive to be present
                ZipFile.ExtractToDirectory(installPackageCacher.FilePath, installPackageDir);
            }

            updateAndExtract.Telemetry.Success = true;
        }
        
        // Our package should contain the unattend.xml file that needs to go in W:\Windows\Panther\unattend.xml
        // We should create the directory if it does not exist
        Directory.CreateDirectory(@"W:\Windows\Panther\");
        
        
        Message = "Copying unattend.xml...";
        var dest = @"W:\Windows\Panther\unattend.xml";
        var source = Path.Combine(installPackageDir, "unattend.xml");
        var fileCopier = new CustomFileCopier(source, dest);
        fileCopier.OnProgressChanged += (long size, long downloaded, double percentage, ref bool cancel) =>
        {
            Progress = percentage;
            Message = $"Copying unattend.xml... {percentage/100:P1} ({downloaded.Bytes().Humanize()} / {size.Bytes().Humanize()})";
        };
        await context.WaitForDriveAsync(); //Wait for the drive to be present
        await fileCopier.CopyAsync();

        var outputPath = @"W:\DotnetInstallScript";
        
        CopyDirectoryStructure(installPackageDir, outputPath);
        foreach (var relativeFilePath in GetRelativeFilePaths(installPackageDir))
        {
            //Output will be outputpath concat relative path
            var dest2 = Path.Combine(outputPath, relativeFilePath);
            var source2 = Path.Combine(installPackageDir, relativeFilePath);
            var fileCopier2 = new CustomFileCopier(source2, dest2);
            fileCopier2.OnProgressChanged += (long size, long downloaded, double percentage, ref bool cancel) =>
            {
                Progress = percentage;
                Message = $"Copying {relativeFilePath}... {percentage/100:P1} ({downloaded.Bytes().Humanize()} / {size.Bytes().Humanize()})";
            };
            await context.WaitForDriveAsync(); //Wait for the drive to be present
            await fileCopier2.CopyAsync();
        }
        
        Message = "Done";
        Progress = 100;
        return new StepResult(true, "Successfully prepared dotnet for installation");
    }
    
    public static IEnumerable<string> GetRelativeFilePaths(string parentDirectory)
    {
        DirectoryInfo dir = new DirectoryInfo(parentDirectory);
        
        foreach (FileInfo file in dir.GetFiles("*", SearchOption.AllDirectories))
        {
            // Get the relative path of the file
            string relativePath = file.FullName.Substring(parentDirectory.Length + 1);
            yield return relativePath;
        }
    }
    
    public static void CopyDirectoryStructure(string sourceDir, string destDir)
    {
        // Get the subdirectories for the specified directory.
        DirectoryInfo dir = new DirectoryInfo(sourceDir);

        if (!dir.Exists)
        {
            throw new DirectoryNotFoundException(
                "Source directory does not exist or could not be found: "
                + sourceDir);
        }

        DirectoryInfo[] dirs = dir.GetDirectories();
        
        // If the destination directory doesn't exist, create it.
        if (!Directory.Exists(destDir))
        {
            Directory.CreateDirectory(destDir);
        }

        // Get the directories in this directory and copy them recursively.
        foreach (DirectoryInfo subdir in dirs)
        {
            string temppath = Path.Combine(destDir, subdir.Name);
            CopyDirectoryStructure(subdir.FullName, temppath);
        }
    }
}