using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Diagnostics;
using System.IO;
using Microsoft.Wim;
using System.Reflection;
using Usb.Events;
using ORMi;
using AutopilotQuick.WMI;
using System.Threading.Tasks;
using System.Windows.Documents;
using AutopilotQuick.Steps;
using Humanizer;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Extensions.Logging;
using Nito.AsyncEx;

namespace AutopilotQuick
{
    class TaskManager {
        private static readonly TaskManager instance = new();
        public static TaskManager getInstance()
        {
            return instance;
        }
        public event EventHandler<CurrentTaskNameChangedEventArgs> CurrentTaskNameChanged;
        public event EventHandler<CurrentTaskMessageChangedEventArgs> CurrentTaskMessageChanged;
        public event EventHandler<CurrentTaskProgressChangedEventArgs> CurrentTaskProgressChanged;
        public event EventHandler<TotalTaskProgressChangedEventArgs> TotalTaskProgressChanged;
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
        public static Cacher wimCache;
        public bool UpdatedImageAvailable = false;
       

        public bool Enabled = false;
        public bool DriveRemoved = false;
        public bool RemoveOnly = false;
        
        private bool TakeHome = false;
        public void ApplyTakeHome(bool Enabled)
        {
            if (Enabled)
            {
                TakeHome = true;
            }
        }

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
        private UserDataContext _context;

        private List<StepBase> Steps = new List<StepBase>()
        {
            new FormatStep(),
            new ApplyImageStep(),
            new DisableTakeHomeStep(),
            new ApplyProductKeyStep(),
            new CleanupRecordsStep(),
            new LogTakeHomeStep(),
            new ApplyDellBiosSettingsStep(),
            new ApplyAutopilotConfigurationStep(),
            new ApplyWifiStep(),
            new MakeDiskBootableStep(),
            new RemoveUnattendXMLStep(),
            new UpdateWinPEStep(),
            new FinalizeSyncingLogsStep(),
            new RebootStep(),
        };
        
        private int CurrentStep = 1;
        public IOperationHolder<RequestTelemetry> TaskManOp;

        public void Run(UserDataContext context, PauseToken pauseToken)
        {
            _context = context;
            if (!Enabled)
            {
                //WimMan.getInstance().Preload();
            }
            
            var telemetryClient = App.GetTelemetryClient();
            TaskManOp = telemetryClient.StartOperation<RequestTelemetry>("Image");
            try
            {
                foreach (var step in Steps)
                {
                    WaitForPause(pauseToken);
                    InvokeCurrentTaskMessageChanged("");
                    InvokeCurrentTaskNameChanged("");
                    InvokeCurrentTaskProgressChanged(0, false);
                    var s = Stopwatch.StartNew();
                    step.StepUpdated += StepOnStepUpdated;
                    var ImageOp = telemetryClient.StartOperation<RequestTelemetry>(step.Name());
                    ImageOp.Telemetry.Success = false;
                    try
                    {
                        var result = step.Run(context, pauseToken, ImageOp).ConfigureAwait(true).GetAwaiter()
                            .GetResult();
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
                            if (!step.Critical)
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
                        if (!step.Critical)
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

        private void StepOnStepUpdated(object? sender, StepBase.StepStatus e)
        {
            double totalProgress = Steps.Average(x => x.Progress);
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
