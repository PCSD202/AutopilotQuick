using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Wim;
using Nito.AsyncEx;
using NLog;

namespace AutopilotQuick.Steps
{
    internal class ApplyImageStep : StepBaseEx
    {
        public readonly Logger Logger = LogManager.GetCurrentClassLogger();


        private WimMessageResult ImageCallback(WimMessageType messageType, object message, object userData)
        {
            // This method is called for every single action during the process being executed.
            // In the case of apply, you'll get Progress, Info, Warnings, Errors, etc
            //
            // The trick is to determine the message type and cast the "message" param to the corresponding type
            //

            switch (messageType)
            {
                case WimMessageType.Progress:  // Some progress is being sent

                    // Get the message as a WimMessageProgress object
                    WimMessageProgress progressMessage = (WimMessageProgress)message;

                    Message = $"Applying image {progressMessage.PercentComplete}%";
                    // Print the progress
                    Progress = progressMessage.PercentComplete;

                    break;

                case WimMessageType.Warning:  // A warning is being sent

                    // Get the message as a WimMessageProgress object
                    WimMessageWarning warningMessage = (WimMessageWarning)message;

                    // Print the file and error code
                   Message = $"Warning: {warningMessage.Path} ({warningMessage.Win32ErrorCode})";

                    break;

                case WimMessageType.Error:  // An error is being sent

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
        public void TaskManager_InternetBecameAvailable(object? sender, EventArgs e)
        {
            if (!_updatedImageAvailable)
            {
                _updatedImageAvailable = !WimMan.getInstance().GetCacherForModel().IsUpToDate;
            }

        }

        public override async Task<StepResult> Run(UserDataContext context, PauseToken pauseToken)
        {
            InternetMan.getInstance().InternetBecameAvailable += TaskManager_InternetBecameAvailable;

            var wimCache = WimMan.getInstance().GetCacherForModel();
            if (IsEnabled)
            {
                Title = "Applying Windows";
                Message = "Starting to apply";
                IsIndeterminate = false;
                try
                {
                    if (!wimCache.FileCached)
                    {
                        InternetMan.getInstance().InternetBecameAvailable -= TaskManager_InternetBecameAvailable;
                        _updatedImageAvailable = false;
                        wimCache.DownloadUpdate();
                        InternetMan.getInstance().InternetBecameAvailable += TaskManager_InternetBecameAvailable;
                    }

                    using (var wimHandle = WimgApi.CreateFile(wimCache.FilePath, WimFileAccess.Read,
                               WimCreationDisposition.OpenExisting, WimCreateFileOptions.None, WimCompressionType.None))
                    {
                        // Always set a temporary path
                        WimgApi.SetTemporaryPath(wimHandle, Environment.GetEnvironmentVariable("TEMP"));

                        // Register a method to be called while actions are performed by WIMGAPi for this .wim file
                        WimgApi.RegisterMessageCallback(wimHandle, ImageCallback);

                        try
                        {
                            // Get a handle to a specific image inside of the .wim
                            using (var imageHandle = WimgApi.LoadImage(wimHandle, 1))
                            {
                                // Apply the image
                                WimgApi.ApplyImage(imageHandle, "W:\\", WimApplyImageOptions.None);
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.Error(ex);
                        }
                        finally
                        {
                            // Be sure to unregister the callback method
                            //
                            WimgApi.UnregisterMessageCallback(wimHandle, ImageCallback);
                        }
                    }
                }
                catch (Exception e)
                {
                    Logger.Error(e);
                }
                if (!wimCache.FileCached)
                {
                    InternetMan.getInstance().InternetBecameAvailable -= TaskManager_InternetBecameAvailable;
                    _updatedImageAvailable = false;
                    wimCache.DownloadUpdate();
                    InternetMan.getInstance().InternetBecameAvailable += TaskManager_InternetBecameAvailable;
                    return await Run(context, pauseToken);
                }
            }
            else
            {
                Title = "Apply image - DISABLED";
                Message = "Will continue after 5 seconds";
                await Task.Run(() => CountDown(pauseToken, 5000));
            }

            return new StepResult(true, "Successfully applied image to drive");
        }
    }
}
