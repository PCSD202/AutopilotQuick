using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Diagnostics;
using System.IO;
using NLog;
using Microsoft.Wim;
using System.Reflection;
using Usb.Events;
using ORMi;
using AutopilotQuick.WMI;
using System.Threading.Tasks;
using System.Windows.Documents;
using AutopilotQuick.Steps;
using Humanizer;
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
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

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
            InvokeCurrentTaskNameChanged("Paused");
            InvokeCurrentTaskMessageChanged("Waiting for unpause");
            InvokeCurrentTaskProgressChanged(0, true);
            pauseToken.WaitWhilePaused();
        }
        private UserDataContext _context;

        private List<StepBase> Steps = new List<StepBase>()
        {
            new FormatStep(),
            new ApplyImageStep(),
            new DisableTakeHomeStep(),
            new ApplyProductKeyStep(),
            new RemoveDeviceFromAutopilotStep(),
            new ApplyDellBiosSettingsStep(),
            new ApplyAutopilotConfigurationStep(),
            new ApplyWifiStep(),
            new MakeDiskBootableStep(),
            new RemoveUnattendXMLStep(),
            new IntuneCleanupStep(),
            //new UpdateWinPEStep(),
            new FinalizeSyncingLogsStep(),
            new RebootStep(),
        };

        private int CurrentStep = 1;

        public void Run(UserDataContext context, PauseToken pauseToken)
        {
            _context = context;
            if (!Enabled)
            {
                //WimMan.getInstance().Preload();
            }
            try
            {
                foreach (var step in Steps)
                {
                    Logger.Info($"Is Paused: {pauseToken.IsPaused}");
                    WaitForPause(pauseToken);
                    InvokeCurrentTaskMessageChanged("");
                    InvokeCurrentTaskNameChanged("");
                    InvokeCurrentTaskProgressChanged(0, false);
                    var s = Stopwatch.StartNew();
                    step.StepUpdated += StepOnStepUpdated;
                    try
                    {
                        var result = step.Run(context, pauseToken).ConfigureAwait(true).GetAwaiter().GetResult();
                        Logger.Info($"Step completed. Success: {result.Success}, Output: {result.Message}");
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
                        Logger.Error(b);
                        if (!step.Critical)
                        {
                            Logger.Info("Continuing to next step because step was not critical");
                            continue;
                        }
                        else
                        {
                            Logger.Info("Cannot continue, step was marked as critical.");
                            InvokeCurrentTaskNameChanged("Failed - Cannot continue");
                            InvokeCurrentTaskMessageChanged($"{b}");
                            break;
                        }
                    }
                    

                    step.StepUpdated -= StepOnStepUpdated;
                    s.Stop();
                    Logger.Info($"Step execution took {s.Elapsed.Humanize(3)}.");
                }
            }
            catch (Exception e)
            {
                Logger.Error(e);
                Logger.Info(e.StackTrace);
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
