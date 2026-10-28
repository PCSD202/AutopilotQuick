#region

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using AQ.SecretArchive;
using Humanizer;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Extensions.Logging;
using Nito.AsyncEx;

#endregion

namespace AutopilotQuick.Steps
{
    internal class ApplyDellBiosSettingsStep : StepBaseEx
    {
        public override string Name() => "Apply dell bios settings step";
        public readonly ILogger Logger = App.GetLogger<ApplyDellBiosSettingsStep>();

        public override async Task<StepResult> Run(UserDataContext context, PauseToken pauseToken, IOperationHolder<RequestTelemetry> stepOperation)
        {
            Title = "Applying dell bios settings";
            if (!IsEnabled)
            {
                Title = "Applying dell bios settings - DISABLED";
                Message = "Will continue after 5 seconds";
                await Task.Run(() => CountDown(pauseToken, 5000));
                return new StepResult(true, "Skipped applying bios settings");
            }

            Message = "Extracting dell bios application";
            if (!App.RunningOnWinPE() || !Directory.Exists(@"X:\"))
                return new StepResult(false, "BIOS settings require WinPE so the decrypted bundle stays on the RAM disk.");
            var dellBiosSettingsDir = Path.Combine(@"X:\AQ\SecretRuntime", Guid.NewGuid().ToString("N"));

            Progress = 0;
            IsIndeterminate = true;

            try
            {
                var bundle = AppSecrets.ReadBytes(SecretNames.DellBiosSettings);
                try
                {
                    using var input = new MemoryStream(bundle, false);
                    using var zip = SecretArchive.ReadZip(input);
                    foreach (var name in zip.Names)
                    {
                        var path = Path.Combine(dellBiosSettingsDir, name.Replace('/', Path.DirectorySeparatorChar));
                        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                        using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                        zip.CopyTo(name, file);
                    }
                }
                finally { CryptographicOperations.ZeroMemory(bundle); }

                IsIndeterminate = false;
                Progress = 25;

                Message = "Figuring out model";
                var scriptExecutable = "LaptopBiosSettings.cmd";
                var model = GetDeviceModel(pauseToken);
                if (model.Contains("Optiplex"))
                {
                    scriptExecutable = "DesktopBiosSettings.cmd";
                }

                if (context.TakeHomeToggleOn)
                {
                    Message = "Removing BIOS setup password for take-home";
                    return await RunBiosProcessAsync(DellBiosCommand.ClearSetupPassword(
                        dellBiosSettingsDir, AppSecrets.ReadText(SecretNames.BiosPassword)));
                }

                Message = $"This device is a {model}, applying bios settings";
                return await RunBiosScriptAsync(dellBiosSettingsDir,
                    "& $env:ComSpec /c " + Quote(scriptExecutable) + "; exit $LASTEXITCODE");
            }
            finally
            {
                if (Directory.Exists(dellBiosSettingsDir)) Directory.Delete(dellBiosSettingsDir, true);
            }
        }

        private static string Quote(string text) => "'" + text.Replace("'", "''") + "'";

        private Task<StepResult> RunBiosScriptAsync(string workingDirectory, string script) =>
            RunBiosProcessAsync(new ProcessStartInfo("powershell.exe", "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command -")
            {
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }, script);

        private async Task<StepResult> RunBiosProcessAsync(ProcessStartInfo startInfo, string? script = null)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(1));
            using var process = new Process { StartInfo = startInfo };
            process.OutputDataReceived += (_, _) => { };
            process.ErrorDataReceived += (_, _) => { };
            try { process.Start(); }
            catch (Win32Exception ex)
            {
                Logger.LogWarning("Could not start BIOS settings process. Windows error code {Code}", ex.NativeErrorCode);
                return new StepResult(false, $"Could not start BIOS settings (Windows error {ex.NativeErrorCode}). Check the Dell bundle's executable and WinPE dependencies.");
            }
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            try
            {
                if (script is not null)
                {
                    await process.StandardInput.WriteLineAsync(script);
                    process.StandardInput.Close();
                }
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                return new StepResult(false, "BIOS settings timed out.");
            }
            finally
            {
                if (!process.HasExited) process.Kill(true);
                await process.WaitForExitAsync();
            }
            Logger.LogInformation("BIOS settings process exited with code {Code}", process.ExitCode);
            Message = "Done";
            Progress = 100;
            return new StepResult(process.ExitCode == 0,
                process.ExitCode == 0 ? "Successfully applied bios settings" :
                process.ExitCode == 106 ? "BIOS settings failed (exit code 106). Dell CCTK rejected a command argument." :
                    $"BIOS settings failed (exit code {process.ExitCode}). Check the device's current BIOS password and the Dell bundle's WinPE drivers/dependencies.");
        }
    }
}
