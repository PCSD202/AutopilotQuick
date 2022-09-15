using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Extensions.Logging;
using Microsoft.Wim;
using Nito.AsyncEx;


namespace AutopilotQuick.Steps
{
    internal class ApplyImageStep : StepBaseEx
    {
        private readonly ILogger Logger = App.GetLogger<ApplyImageStep>();
        public override string Name() => "Apply image step";

        private double? CalculatedWeight;

        public override double ProgressWeight()
        {
            if (CalculatedWeight.HasValue) return CalculatedWeight.Value;
            
            var stepList = TaskManager.GetInstance().Steps;
            var indexOfMe = stepList.FindIndex(x => x.Name() == Name());
            var stepListWithoutMe = new List<StepBase>(stepList);
            stepListWithoutMe.RemoveAt(indexOfMe);
            var stepListBeforeMe = stepList.GetRange(0, indexOfMe);
            double endProgress = 85; //When I am at 100% progress, I want the total progress to be 85
            // We need to find out what weight I need to be at
            var weightedProgressWithoutMe = stepListBeforeMe.Sum(x => 100 * x.ProgressWeight()); //Calculate it like the steps before me are at 100%
            var weightsNotIncludingMine = stepListWithoutMe.Sum(x => x.ProgressWeight());

            var myWeightNeedsToBe = 1d / 15d * (endProgress * weightsNotIncludingMine - weightedProgressWithoutMe);
            
            //The idea is that (100*x+weightedProgressWithoutMe) / (x+weightsNotIncludingMine) = endProgress
            CalculatedWeight = myWeightNeedsToBe;
            return myWeightNeedsToBe;
        }

        private WimMessageResult ImageCallback(WimMessageType messageType, object message, object userData)
        {
            // This method is called for every single action during the process being executed.
            // In the case of apply, you'll get Progress, Info, Warnings, Errors, etc
            //
            // The trick is to determine the message type and cast the "message" param to the corresponding type
            //

            switch (messageType)
            {
                case WimMessageType.Progress: // Some progress is being sent

                    // Get the message as a WimMessageProgress object
                    WimMessageProgress progressMessage = (WimMessageProgress)message;

                    Message = $"Applying image {progressMessage.PercentComplete}%";
                    // Print the progress
                    Progress = progressMessage.PercentComplete;

                    break;

                case WimMessageType.Warning: // A warning is being sent

                    // Get the message as a WimMessageProgress object
                    WimMessageWarning warningMessage = (WimMessageWarning)message;

                    // Print the file and error code
                    Message = $"Warning: {warningMessage.Path} ({warningMessage.Win32ErrorCode})";

                    break;

                case WimMessageType.Error: // An error is being sent

                    // Get the message as a WimMessageError object
                    WimMessageError errorMessage = (WimMessageError)message;

                    // Print the file and error code
                    Message = ($"Error: {errorMessage.Path} ({errorMessage.Win32ErrorCode})");
                    break;
            }

            // Depending on what this method returns, the WIMGAPI will continue or cancel.
            //
            // Return WimMessageResult.Abort to cancel.  In this case we return Success so WIMGAPI keeps going
            return _updatedImageAvailable ? WimMessageResult.Abort : WimMessageResult.Success;
        }

        private bool _updatedImageAvailable = false;

        //When internet becomes available, check and see if any updates are available
        public void TaskManager_InternetBecameAvailable(object? sender, EventArgs e)
        {
            if (!_updatedImageAvailable)
            {
                _updatedImageAvailable = !WimMan.getInstance().GetCacherForModel().IsUpToDate;
            }
        }

        private record struct ScratchDir(string Path, bool Success);

        private ScratchDir MakeScratchDir()
        {
            var scratchDir = Path.Join("W:\"", "Scratch");
            if (!Directory.Exists("W:\\"))
            {
                Logger.LogError("W:\\ Drive does not exist, format must have failed");
                return new ScratchDir("", false);
            }

            try
            {
                if (Directory.Exists(scratchDir))
                {
                    Directory.Delete(scratchDir, true); //Remove the scratch dir for re-creation
                }

                Logger.LogInformation("Created directory {scratchDir} for DISM scratch directory", scratchDir);
                Directory.CreateDirectory(scratchDir);
                return new ScratchDir(scratchDir, true);
            }
            catch (Exception e)
            {
                Logger.LogError(e, "Got exception {e} while trying to make scratch directory", e);
                return new ScratchDir("", false);
            }
        }

        public override async Task<StepResult> Run(UserDataContext context, PauseToken pauseToken,
            IOperationHolder<RequestTelemetry> StepOperation)
        {
            if (!InternetMan.GetInstance().IsConnected)
            {
                InternetMan.GetInstance().InternetBecameAvailable += TaskManager_InternetBecameAvailable;
            }
            else
            {
                TaskManager_InternetBecameAvailable(this, EventArgs.Empty);
            }

            var wimCache = WimMan.getInstance().GetCacherForModel();
            if (!IsEnabled)
            {
                Title = "Apply image - DISABLED";
                Message = "Will continue after 5 seconds";
                if (!wimCache.FileCached || !wimCache.IsUpToDate)
                {
                    InternetMan.GetInstance().InternetBecameAvailable -= TaskManager_InternetBecameAvailable;
                    _updatedImageAvailable = false;
                    await wimCache.DownloadUpdateAsync();
                    InternetMan.GetInstance().InternetBecameAvailable += TaskManager_InternetBecameAvailable;
                }

                await Task.Run(() => CountDown(pauseToken, 5000));
                return new StepResult(true, "Apply Image step Disabled");
            }

            Title = "Applying Windows";
            Message = "Starting to apply";
            IsIndeterminate = false;
            try
            {
                //If file is not cached, make sure not to accidentally trigger the InternetBecameAvailable event
                //Make sure that we set UpdatedImagedAvailable to false if the internet event already fired
                //Download the update, and resubscribe
                if (!wimCache.FileCached)
                {
                    InternetMan.GetInstance().InternetBecameAvailable -= TaskManager_InternetBecameAvailable;
                    _updatedImageAvailable = false;
                    await wimCache.DownloadUpdateAsync();
                    InternetMan.GetInstance().InternetBecameAvailable += TaskManager_InternetBecameAvailable;
                }

                var scratchDir = MakeScratchDir();
                if (!scratchDir.Success)
                {
                    return new StepResult(false,
                        "Failed to create scratch directory. This could mean that the drive in the computer is faulty.");
                }

                using var wimHandle = WimgApi.CreateFile(wimCache.FilePath, WimFileAccess.Read,
                    WimCreationDisposition.OpenExisting, WimCreateFileOptions.None, WimCompressionType.None);

                // Always set a temporary path
                WimgApi.SetTemporaryPath(wimHandle, scratchDir.Path);

                // Register a method to be called while actions are performed by WIMGAPi for this .wim file
                WimgApi.RegisterMessageCallback(wimHandle, ImageCallback);

                try
                {
                    // Get a handle to a specific image inside of the .wim
                    using var imageHandle = WimgApi.LoadImage(wimHandle, 1);
                    // Apply the image
                    WimgApi.ApplyImage(imageHandle, "W:\\", WimApplyImageOptions.None);
                }
                catch (OperationCanceledException ex)
                {
                    Logger.LogInformation("Operation was canceled, we must have an update");
                }
                catch (Win32Exception ex)
                {
                    Logger.LogError(ex, "Got error {ex} while applying windows", ex);
                    return new StepResult(false, $"Got error {ex} while applying windows");
                }
                finally
                {
                    // Be sure to unregister the callback method
                    WimgApi.UnregisterMessageCallback(wimHandle, ImageCallback);
                }
            }
            catch (Win32Exception e)
            {
                Logger.LogError(e, "Caught error while applying windows");
                await InternetMan.WaitForInternetAsync(context);
                wimCache.Delete(); //Delete and re-download it because we had an issue with it
                return await Run(context, pauseToken, StepOperation);
            }

            if (wimCache.FileCached && !_updatedImageAvailable)
                return new StepResult(true, "Successfully applied image to drive");

            InternetMan.GetInstance().InternetBecameAvailable -= TaskManager_InternetBecameAvailable;
            _updatedImageAvailable = false;
            await wimCache.DownloadUpdateAsync();
            InternetMan.GetInstance().InternetBecameAvailable += TaskManager_InternetBecameAvailable;
            return await Run(context, pauseToken, StepOperation);
        }
    }
}