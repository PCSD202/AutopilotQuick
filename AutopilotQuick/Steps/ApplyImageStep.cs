#region

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Humanizer;
using Humanizer.Localisation;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Extensions.Logging;
using Microsoft.Wim;
using Nito.AsyncEx;

#endregion

namespace AutopilotQuick.Steps
{
    internal class ApplyImageStep : StepBaseEx
    {
        private readonly ILogger Logger = App.GetLogger<ApplyImageStep>();
        public override string Name() => "Apply image step";

        private double? CalculatedWeight;

        private bool StopBecauseOfTimeChange = false;

        public override double ProgressWeight()
        {
            if (CalculatedWeight.HasValue) return CalculatedWeight.Value;
            
            var stepList = TaskManager.GetInstance().Steps;
            var indexOfMe = stepList.FindIndex(x => x.Name() == Name());
            var stepListWithoutMe = new List<StepBase>(stepList);
            stepListWithoutMe.RemoveAt(indexOfMe);
            var stepListBeforeMe = stepList.GetRange(0, indexOfMe);
            double endProgress = 50; //When I am at 100% progress, I want the total progress to be 85
            // We need to find out what weight I need to be at
            var weightedProgressWithoutMe = stepListBeforeMe.Sum(x => 100 * x.ProgressWeight()); //Calculate it like the steps before me are at 100%
            var weightsNotIncludingMine = stepListWithoutMe.Sum(x => x.ProgressWeight());

            //var myWeightNeedsToBe = 1d / 15d * (endProgress * weightsNotIncludingMine - weightedProgressWithoutMe);
            var myWeightNeedsToBe = (weightedProgressWithoutMe - (endProgress * weightsNotIncludingMine)) / (endProgress - 100);
            
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
            var scratchDir = Path.Join("W:\\", "Scratch");
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

        /// <summary>
        /// Copies the specified cached wim to the drive
        /// </summary>
        /// <param name="wimCache">Cached file to copy</param>
        /// <returns>The path to the wim on the drive</returns>
        public async Task<string> CopyWimToDrive(Cacher wimCache)
        {
            Title = "Copying WIM to drive";
            var dest = Path.Join("W:\\", wimCache.FileName);
            var source = wimCache.FilePath;
            var copier = new CustomFileCopier(source, dest);
            var sw = new Stopwatch();
            copier.OnProgressChanged += (long size, long downloaded, double percentage, ref bool cancel) =>
            {
                var bytesPerSecond = downloaded.Bytes().Per(sw.Elapsed);
                Progress = percentage/2;
                Message = $"Copying {percentage / 100:P} {downloaded.Bytes().Humanize("#.00")} of {size.Bytes().Humanize("#.00")} ({bytesPerSecond.Humanize("#", TimeUnit.Second)})";
            };
            sw = Stopwatch.StartNew();
            await copier.CopyAsync();
            return dest;
        }


        private record WimHandleResult(bool Success, WimHandle Handle);
        private WimHandleResult GetWimHandle(string path, WimFileAccess desiredAccess, WimCreationDisposition creationDisposition, WimCreateFileOptions options, WimCompressionType compressionType)
        {
            try
            {
                if (path.EndsWith("esd"))
                {
                    var handle = WimgApi.CreateFile(path, desiredAccess, creationDisposition, (WimCreateFileOptions)((uint)options | 0x20000000u), compressionType);
                    return new WimHandleResult(true, handle);
                }
                else
                {
                    var handle = WimgApi.CreateFile(path, desiredAccess, creationDisposition, options, compressionType);
                    return new WimHandleResult(true, handle);
                }
                
            }
            catch (Win32Exception)
            {
                return new WimHandleResult(false, WimHandle.Null);
            }
        }
        
        

        private async Task<string> WriteTestFile(string Destination, double TestFileSizeBytes)
        {

            int targetLineSize = (int)1.Megabytes().Bytes;
            ulong counter = 0;
            var bandwidth = new Bandwidth();
            //Lets generate a text file on the flash drive with some dummy data in it and then collect the hash
            var hasher = MD5.Create();
            using (FileStream outFile = File.Create(Destination, (int)4.Kilobytes().Bytes,  FileOptions.SequentialScan))
            using (CryptoStream crypto = new CryptoStream(outFile, hasher, CryptoStreamMode.Write))
            using (StreamWriter writer = new StreamWriter(crypto, Encoding.UTF8, (int)1.Megabytes().Bytes, false))
            {
                DateTime LastUpdate = DateTime.MinValue;
                
                foreach (var stringToWrite in Enumerable.Repeat(0, Int32.MaxValue)
                             .Select(x=>
                             {
                                 var singleString = App.Base64Encode(Guid.NewGuid().ToString());
                                 var stringSize = Encoding.UTF8.GetByteCount(singleString);

                                 var repeatsNeeded = targetLineSize / stringSize;

                                 return string.Join(singleString, new string[repeatsNeeded + 1]);
                             }).TakeWhile(x=> outFile.Length < TestFileSizeBytes ))
                {
                    counter++;
                    
                    await writer.WriteLineAsync(stringToWrite.ToString());
                    #region Fancy display

                    bandwidth.CalculateSpeed(stringToWrite.ToString().Length * sizeof(char));
                    var now = DateTime.UtcNow;
                    if ((now - LastUpdate).TotalMilliseconds >= 50)
                    {
                        LastUpdate = now;
                        var eta = "calculating...";
                        if (bandwidth.AverageSpeed > 0)
                        {
                            eta = ((TestFileSizeBytes - outFile.Length) / bandwidth.AverageSpeed).Seconds()
                                .Humanize(minUnit: TimeUnit.Second, precision: 2);
                        }

                        const int space = 4;
                        var info = new List<KeyValuePair<string, string>>()
                        {
                            new("Time left:", eta),
                            new("Written:",
                                $"{outFile.Length.Bytes().Humanize("#.00")} of {TestFileSizeBytes.Bytes().Humanize("#.00")}"),
                            new("Speed:",
                                $"{bandwidth.Speed.Bytes().Per(1.Seconds()).Humanize("#")} (avg: {bandwidth.AverageSpeed.Bytes().Per(1.Seconds()).Humanize("#")})")
                        };
                        var longest = info.MaxBy(x => x.Key.Length).Key.Length - 1;
                        var maxLength = longest + space;
                        var sb = new StringBuilder();
                        sb.AppendLine("Writing test file to drive");
                        foreach (var pair in info)
                        {
                            var newKey = pair.Key.PadRight(maxLength + 2);

                            if (pair.Key.Length == longest)
                            {
                                newKey = pair.Key.PadRight(maxLength);
                            }

                            sb.AppendLine($"{newKey} {pair.Value}");
                        }


                        var tempProgress = (outFile.Length / TestFileSizeBytes) * 100;
                        Progress = tempProgress / 2;

                        var builtMessage = sb.ToString();
                        if (builtMessage != Message)
                        {
                            Message = builtMessage;
                        }
                    }

                    #endregion

                }
            }
            
            Logger.LogInformation($"Wrote {counter} lines");
            // at this point the streams are closed so the hash is ready
            string hash = BitConverter.ToString(hasher.Hash).Replace("-", "").ToLowerInvariant();
            return hash;
        }

        private async Task<string> ReadTestFile(string Source, double FileSize)
        {
            var bandwidth = new Bandwidth();
            byte[] buffer = new byte[1024 * 1024]; // 1MB buffer
            DateTime LastUpdate = DateTime.MinValue;
            var hasher = MD5.Create();
            
            using (FileStream source = new FileStream(Source, FileMode.Open, FileAccess.Read))
            using (CryptoStream dest = new CryptoStream(Stream.Null, hasher, CryptoStreamMode.Write))
            {
                var fileLength = source.Length;
                long totalBytes = 0;
                var currentBlockSize = 0;

                while ((currentBlockSize = source.Read(buffer, 0, buffer.Length)) > 0)
                {
                    totalBytes += currentBlockSize;
                    double percentage = (double)totalBytes * 100.0 / fileLength;

                    #region Fancy display

                    bandwidth.CalculateSpeed(currentBlockSize);
                    var now = DateTime.UtcNow;
                    if ((now - LastUpdate).TotalMilliseconds >= 20)
                    {
                        LastUpdate = now;
                        var eta = "calculating...";
                        if (bandwidth.AverageSpeed > 0)
                        {
                            eta = ((FileSize - totalBytes) / bandwidth.AverageSpeed).Seconds()
                                .Humanize(minUnit: TimeUnit.Second, precision: 2);
                        }

                        const int space = 4;
                        var info = new List<KeyValuePair<string, string>>()
                        {
                            new("Time left:", eta),
                            new("Read:",
                                $"{totalBytes.Bytes().Humanize("#.00")} of {fileLength.Bytes().Humanize("#.00")}"),
                            new("Speed:",
                                $"{bandwidth.Speed.Bytes().Per(1.Seconds()).Humanize("#")} (avg: {bandwidth.AverageSpeed.Bytes().Per(1.Seconds()).Humanize("#")})")
                        };
                        var longest = info.MaxBy(x => x.Key.Length).Key.Length - 1;
                        var maxLength = longest + space;
                        var sb = new StringBuilder();
                        sb.AppendLine("Reading test file from drive");
                        foreach (var pair in info)
                        {
                            var newKey = pair.Key.PadRight(maxLength + 2);

                            if (pair.Key.Length == longest)
                            {
                                newKey = pair.Key.PadRight(maxLength);
                            }

                            sb.AppendLine($"{newKey} {pair.Value}");
                        }

                        
                        Progress = percentage  / 2 + 50;

                        var builtMessage = sb.ToString();
                        if (builtMessage != Message)
                        {
                            Message = builtMessage;
                        }
                    }

                    #endregion
                    
                    await dest.WriteAsync(buffer, 0, currentBlockSize);
                }
            }
            // at this point the streams are closed so the hash is ready
            string hash = BitConverter.ToString(hasher.Hash).Replace("-", "").ToLowerInvariant();
            return hash;
        }

        public async Task<bool> TestWindowsDrive()
        {
            Title = "Testing SSD";
            Progress = 0;
            var TestFileSizeBytes = 1.Gigabytes().Bytes;
            var Drive = @"W:\";

            //First lets make sure that the W drive exists.
            if (!Directory.Exists(Drive))
            {
                Logger.LogWarning($"The {Drive} drive does not exist");
                return false;
            }

            var DestinationFile = Path.Join(Drive, "test.txt");


            try
            {
                var correctHash = await WriteTestFile(DestinationFile, TestFileSizeBytes);
                Logger.LogInformation($"Correct hash: {correctHash}");

                var readHash = await ReadTestFile(DestinationFile, TestFileSizeBytes);
                Logger.LogInformation(
                    $"Correct hash: '{correctHash}', Read hash: '{readHash}', equal? {correctHash == readHash}");
                File.Delete(DestinationFile); //Remove file because we're done with it
                return correctHash == readHash;
            }
            catch (Exception e)
            {
                Logger.LogError("Got error {e} while testing SSD. Assumed bad.", e);
                return false;
            }
        }
        
        public override async Task<StepResult> Run(UserDataContext context, PauseToken pauseToken, IOperationHolder<RequestTelemetry> StepOperation)
        {
            InternetMan.GetInstance().InternetBecameAvailable -= TaskManager_InternetBecameAvailable;
            if (!InternetMan.GetInstance().IsConnected)
            {
                InternetMan.GetInstance().InternetBecameAvailable += TaskManager_InternetBecameAvailable;
            }
            else
            {
                TaskManager_InternetBecameAvailable(this, EventArgs.Empty);
            }

            var wimCache = WimMan.getInstance().GetCacherForModel();
            var wimIndex = WimMan.getInstance().GetImageIndexForModel();
            
            if (!IsEnabled)
            {
                Title = $"Apply image - DISABLED - {wimIndex}";
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
            Message = "Checking image...";
            IsIndeterminate = false;
            
            //If file is not cached, make sure not to accidentally trigger the InternetBecameAvailable event
            //Make sure that we set UpdatedImagedAvailable to false if the internet event already fired
            //Download the update, and resubscribe
            if (!wimCache.FileCached)
            {
                Logger.LogInformation("Image was not cached, we need to download it");
                InternetMan.GetInstance().InternetBecameAvailable -= TaskManager_InternetBecameAvailable;
                _updatedImageAvailable = false;
                await wimCache.DownloadUpdateAsync();
                InternetMan.GetInstance().InternetBecameAvailable += TaskManager_InternetBecameAvailable;
            }
            
            Message = "Creating scratch directory...";
            var scratchDir = MakeScratchDir();
            if (!scratchDir.Success)
            {
                return new StepResult(false, "Failed to create scratch directory. This could mean that the drive in the computer is faulty.");
            }

            Message = "Opening image...";
            await context.WaitForDriveAsync();
            var handleResult = GetWimHandle(wimCache.FilePath, WimFileAccess.Read, WimCreationDisposition.OpenExisting, WimCreateFileOptions.None, WimCompressionType.Lzms);
            while (!handleResult.Success)
            {
                //Something must be wrong with our image. Lets delete it and re-download it
                InternetMan.GetInstance().InternetBecameAvailable -= TaskManager_InternetBecameAvailable; //Unsubscribe
                _updatedImageAvailable = false;
                await wimCache.DownloadUpdateAsync();
                handleResult = GetWimHandle(wimCache.FilePath, WimFileAccess.Read, WimCreationDisposition.OpenExisting, WimCreateFileOptions.None, WimCompressionType.Lzms);
                InternetMan.GetInstance().InternetBecameAvailable += TaskManager_InternetBecameAvailable; //Resubscribe
            }
            using var wimHandle = handleResult.Handle;

            // Always set a temporary path
            WimgApi.SetTemporaryPath(wimHandle, scratchDir.Path);

            // Register a method to be called while actions are performed by WIMGAPi for this .wim file
            WimgApi.RegisterMessageCallback(wimHandle, ImageCallback);
            
            try
            {
                Message = "Reading image...";
                // Get a handle to a specific image inside of the .wim
                await context.WaitForDriveAsync();
                using var imageHandle = WimgApi.LoadImage(wimHandle, wimIndex);
                Message = "Starting to apply...";
                // Apply the image
                await context.WaitForDriveAsync();
                WimgApi.ApplyImage(imageHandle, "W:\\", WimApplyImageOptions.Index);
            }
            catch (OperationCanceledException ex)
            {
                if(!wimHandle.IsClosed) {wimHandle.Close();}
                Logger.LogInformation("Operation was canceled, we must have an update");
                
                InternetMan.GetInstance().InternetBecameAvailable -= TaskManager_InternetBecameAvailable;
                _updatedImageAvailable = false;
                wimCache = WimMan.getInstance().GetCacherForModel();
                await wimCache.DownloadUpdateAsync();
                
                return await Run(context, pauseToken, StepOperation);
            }
            catch (Win32Exception ex)
            {
                if(!wimHandle.IsClosed) {wimHandle.Close();}
                Logger.LogError(ex, "Got error {ex} while applying windows", ex);
                
                return new StepResult(false, "Got error while applying windows.\nThis is due to a bad or failing SSD.");
            }
            finally
            {
                try
                {
                    // Be sure to unregister the callback method
                    WimgApi.UnregisterMessageCallback(wimHandle, ImageCallback);
                    if (!wimHandle.IsClosed)
                    {
                        wimHandle.Close();
                    }
                }
                catch (Exception)
                {
                    Logger.LogWarning("Failed to unregister message callback. Maybe WimHandle is already disposed? WimHandle Closed: {closed}", wimHandle.IsClosed);
                }
                
            }

            return new StepResult(true, "Successfully applied image to drive");
        }
    }
}