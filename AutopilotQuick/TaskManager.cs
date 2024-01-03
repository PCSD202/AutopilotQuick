#region

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AutopilotQuick.Steps;
using Humanizer;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Extensions.Logging;
using Nito.AsyncEx;

#endregion

namespace AutopilotQuick
{
    class TaskManager {
        private static readonly TaskManager Instance = new();
        public static TaskManager GetInstance()
        {
            return Instance;
        }
        public event EventHandler<CurrentTaskNameChangedEventArgs>? CurrentTaskNameChanged;
        public event EventHandler<CurrentTaskMessageChangedEventArgs>? CurrentTaskMessageChanged;
        public event EventHandler<CurrentTaskProgressChangedEventArgs>? CurrentTaskProgressChanged;
        public event EventHandler<TotalTaskProgressChangedEventArgs>? TotalTaskProgressChanged;
        private static readonly ILogger Logger = App.GetLogger<TaskManager>();

        private void InvokeTotalTaskProgressChanged(double progress, bool isIndeterminate = false)
        {
            TotalTaskProgressChanged?.Invoke(this, new TotalTaskProgressChangedEventArgs()
            {
                StepMessage = "",
                Progress = progress,
                isIndeterminate = isIndeterminate
            });
        }
        private void InvokeCurrentTaskNameChanged(string newName)
        {
            CurrentTaskNameChanged?.Invoke(this, new CurrentTaskNameChangedEventArgs()
            {
                Name = newName
            });
        }
        private void InvokeCurrentTaskMessageChanged(string newMessage)
        {
            CurrentTaskMessageChanged?.Invoke(this, new CurrentTaskMessageChangedEventArgs()
            {
                Message = newMessage
            });
        }
        private void InvokeCurrentTaskProgressChanged(double newProgressPercent, bool isIndeterminate = false)
        {
            CurrentTaskProgressChanged?.Invoke(this, new CurrentTaskProgressChangedEventArgs()
            {
                isIndeterminate = isIndeterminate,
                Progress = newProgressPercent
            });
        }
        public bool Enabled => App.Enabled;
        
        private void WaitForPause(PauseToken pauseToken) {
            if (!pauseToken.IsPaused) return;
            using (App.telemetryClient.StartOperation<RequestTelemetry>("Paused"))
            {
                InvokeCurrentTaskNameChanged("Paused");
                InvokeCurrentTaskMessageChanged("Waiting for unpause");
                InvokeCurrentTaskProgressChanged(0, true);
                pauseToken.WaitWhilePaused();
            }
        }

        public List<StepBase> Steps = new List<StepBase>()
        {
            new MaintenanceStep(),
            new FormatStep(),
            new ApplyImageStep(),
            new DisableTakeHomeStep(),
            new ApplyProductKeyStep(),
            new CleanupRecordsStep(),
            new LogTakeHomeStep(),
            //new BiosUpdateStep(), //Disabled because it prompts for password and tasks a while
            new InstallHardwareTester(),
            new InstallDotNetStep(),
            new ApplyDellBiosSettingsStep(),
            new ApplyWifiStep(),
            new MakeDiskBootableStep(),
            //new RemoveUnattendXMLStep(),
            new SharedPCGroupStep(),
            new MakeSureAutopilotSyncedStep(),
            new ApplyAutopilotConfigurationStep(),
            new UpdateWinPEStep(),
            //new FinalizeSyncingLogsStep(),
            new RebootStep(),
        };
        
        public IOperationHolder<RequestTelemetry> TaskManOp;

        public async Task Run(UserDataContext context, PauseToken pauseToken)
        {
            if (!Enabled)
            {
                //await WimMan.getInstance().GetCacherForModel().DownloadUpdateAsync();
            }
            
            var telemetryClient = App.GetTelemetryClient();
            TaskManOp = telemetryClient.StartOperation<RequestTelemetry>("Image");
            TaskManOp.Telemetry.Success = false;
            try
            {
                foreach (var step in Steps)
                {
                    WaitForPause(pauseToken);
                    await context.WaitForDriveAsync(); //Wait for the drive to be present for each step before starting it
                    InvokeCurrentTaskMessageChanged("");
                    InvokeCurrentTaskNameChanged("");
                    InvokeCurrentTaskProgressChanged(0, false);
                    var s = Stopwatch.StartNew();
                    step.StepUpdated += StepOnStepUpdated;
                    var ImageOp = telemetryClient.StartOperation<RequestTelemetry>(step.Name());
                    ImageOp.Telemetry.Success = false;
                    try
                    {
                        var result = await step.Run(context, pauseToken, ImageOp).ConfigureAwait(false);
                        ImageOp.Telemetry.Success = result.Success;
                        telemetryClient.TrackEvent(step.Name() + " completed",
                            new Dictionary<string, string>() { { "Output", result.Message } });

                        Logger.LogInformation($"Step completed. Success: {result.Success}, Output: {result.Message}");
                        step.Progress = 100;
                        if (result.Success)
                        {
                            InvokeCurrentTaskMessageChanged(result.Message);
                            Thread.Sleep(500);
                        }
                        else
                        {
                            if (!step.IsCritical())
                            {
                                InvokeCurrentTaskNameChanged("Failed");
                                InvokeCurrentTaskMessageChanged(result.Message);
                            }
                            else
                            {
                                InvokeCurrentTaskNameChanged("Failed - Cannot continue");
                                InvokeCurrentTaskMessageChanged(result.Message);
                                break;

                            }
                        }
                    }
                    catch (Exception b)
                    {
                        telemetryClient.TrackException(b);
                        if (!step.IsCritical())
                        {
                            Logger.LogInformation("Continuing to next step because step was not critical");
                            continue;
                        }
                        else
                        {
                            Logger.LogInformation("Cannot continue, step was marked as critical.");
                            InvokeCurrentTaskNameChanged("Failed - Cannot continue");
                            InvokeCurrentTaskMessageChanged($"{b}");
                            break;
                        }
                    }
                    finally
                    {
                        ImageOp.Dispose();
                    }


                    step.StepUpdated -= StepOnStepUpdated;
                    s.Stop();
                    Logger.LogInformation($"Step execution took {s.Elapsed.Humanize(3)}.");
                }
            }
            catch (Exception e)
            {
                App.GetTelemetryClient().TrackException(e);
                Logger.LogInformation(e.StackTrace);
            }
            finally
            {
                TaskManOp.Dispose();
            }
            
        }

        private static double CalculateWeightedTotalProgress(IReadOnlyCollection<StepBase> stepsToCalculateOn)
        {
            var weightedProgress = stepsToCalculateOn.Sum(x => x.Progress * x.ProgressWeight());
            var weightTotal = stepsToCalculateOn.Sum(x => x.ProgressWeight());
            return weightedProgress / weightTotal;
        }

        private void StepOnStepUpdated(object? sender, StepBase.StepStatus e)
        {
            //double totalProgress = Steps.Average(x => x.Progress);
            var totalProgress = CalculateWeightedTotalProgress(Steps);
            
            InvokeTotalTaskProgressChanged(totalProgress);
            InvokeCurrentTaskMessageChanged(e.Message);
            InvokeCurrentTaskNameChanged(e.Title);
            InvokeCurrentTaskProgressChanged(e.Progress, e.IsIndeterminate);
        }
    }
    public class CurrentTaskNameChangedEventArgs : EventArgs
    {
        public string Name;
    }
    public class CurrentTaskMessageChangedEventArgs : EventArgs
    {
        public string Message;
    }
    public class CurrentTaskProgressChangedEventArgs : EventArgs
    {
        public double Progress;
        public bool isIndeterminate;
    }
    public class TotalTaskProgressChangedEventArgs : EventArgs
    {
        public string StepMessage;
        public double Progress;
        public bool isIndeterminate;
    }

}
