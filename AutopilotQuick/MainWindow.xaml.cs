using MahApps.Metro.Controls.Dialogs;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using Humanizer;
using System.IO;
using System.IO.Compression;
using System.Diagnostics;
using PgpCore;
using System.Security.Cryptography;
using System.Windows.Forms;
using AutopilotQuick.LogMan;
using MahApps.Metro.Controls;
using Nito.AsyncEx;
using Application = System.Windows.Application;

namespace AutopilotQuick
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        public UserDataContext context;
        private readonly bool Updated;
        private readonly PauseTokenSource _taskManagerPauseTokenSource = new ();
        
        public MainWindow()
        {
            InitializeComponent();
            context = new UserDataContext(DialogCoordinator.Instance);
            DataContext = context;
            WimMan.getInstance().SetContext(context);
            var appFolder = Path.GetDirectoryName(Environment.ProcessPath);
            var appName = Path.GetFileNameWithoutExtension(Environment.ProcessPath);
            var appExtension = Path.GetExtension(Environment.ProcessPath);
            var archivePath = Path.Combine(appFolder, appName + "_Old" + appExtension);
            if (File.Exists(archivePath))
            {
                Updated = true;
                try
                {
                    //Will wait for the other program to exit.
                    var me = Process.GetCurrentProcess();
                    var aProcs = Process.GetProcessesByName(me.ProcessName);
                    aProcs = aProcs.Where(x => x.Id != me.Id).ToArray();
                    if (aProcs != null && aProcs.Length > 0) aProcs[0].WaitForExit(1000);

                    File.Delete(archivePath);
                }
                catch (Exception e)
                {
                    Console.WriteLine("Could not delete old file.");
                }
            }
            else
            {
                Updated = false;
            }
            

        }

        public static bool CheckForInternetConnection(int timeoutMs = 10000, string url = "http://www.gstatic.com/generate_204")
        {
            try
            {
                var request = (HttpWebRequest)WebRequest.Create(url);
                request.KeepAlive = false;
                request.Timeout = timeoutMs;
                using (var response = (HttpWebResponse)request.GetResponse())
                    return true;
            }
            catch
            {
                return false;
            }
        }

        public async void WaitForInternet()
        {
            
            var connectedToInternet = CheckForInternetConnection();
            if (!connectedToInternet)
            {
                var progressController = await context.DialogCoordinator.ShowProgressAsync(context, "Please wait...", "Connecting to the internet");
                progressController.SetIndeterminate();
                while (!connectedToInternet)
                {
                    connectedToInternet = CheckForInternetConnection();
                }
                await progressController.CloseAsync();
            }
            
        }

        private void MetroWindow_Loaded(object sender, RoutedEventArgs e)
        {
            Task.Factory.StartNew(() => DurableAzureBackgroundTask.getInstance().Run(context));
            Application.Current.Exit += (o, args) =>
            {
                DurableAzureBackgroundTask.getInstance().ShouldStop = true;
                BatteryMan.getInstance().ShouldStop = true;
                Environment.Exit(0);
            };


            BatteryMan.getInstance().BatteryUpdated += MainWindow_BatteryUpdated;
            TaskManager.getInstance().TotalTaskProgressChanged += MainWindow_TotalTaskProgressChanged;
            TaskManager.getInstance().CurrentTaskProgressChanged += MainWindow_CurrentTaskProgressChanged;
            TaskManager.getInstance().CurrentTaskMessageChanged += MainWindow_CurrentTaskMessageChanged;
            TaskManager.getInstance().CurrentTaskNameChanged += MainWindow_CurrentTaskNameChanged;
            Task.Factory.StartNew(() => TaskManager.getInstance().Run(context, _taskManagerPauseTokenSource.Token));
            InternetMan.getInstance().InternetBecameAvailable += MainWindow_InternetBecameAvailable;
            Task.Factory.StartNew(() => InternetMan.getInstance().RunLoop());
            Task.Factory.StartNew(() => BatteryMan.getInstance().RunLoop());
        }

        private void MainWindow_BatteryUpdated(object? sender, BatteryMan.BatteryUpdatedEventData e)
        {
            context.IsCharging = e.IsCharging;
            context.BatteryPercent = e.BatteryPercent;
        }

        private void MainWindow_CurrentTaskNameChanged(object? sender, CurrentTaskNameChangedEventArgs e)
        {
            context.CurrentStepName = e.Name;
        }

        private void MainWindow_CurrentTaskMessageChanged(object? sender, CurrentTaskMessageChangedEventArgs e)
        {
            context.CurrentStepMessage = e.Message;
        }

        private void MainWindow_CurrentTaskProgressChanged(object? sender, CurrentTaskProgressChangedEventArgs e)
        {
            context.CurrentStepProgress = e.Progress;
            context.CurrentStepIndeterminate = e.isIndeterminate;
        }

        private void MainWindow_TotalTaskProgressChanged(object? sender, TotalTaskProgressChangedEventArgs e)
        {
            context.TotalProgress = e.Progress;
            context.TotalStepIndeterminate = e.isIndeterminate;
            context.TotalStepMessage = e.StepMessage;
        }

        private void MainWindow_InternetBecameAvailable(object? sender, EventArgs e)
        {
            context.ConnectedToInternet = true;
            context.RefreshLatestVersion();
            Task.Factory.StartNew(Update, TaskCreationOptions.LongRunning);
        }
        private async void ToggleSwitch_Toggled(object sender, RoutedEventArgs e)
        {
            ToggleSwitch toggleSwitch = sender as ToggleSwitch;
            if (toggleSwitch != null && toggleSwitch.IsOn) {
                _taskManagerPauseTokenSource.IsPaused = true;
                var result = await context.DialogCoordinator.ShowMessageAsync(context, "Apply take home?", "This removes the bios password, de-registers the device from autopilot, and does not apply autopilot or wifi config. If confirmed, this can only be undone by re-imaging. Would you like to continue?", MessageDialogStyle.AffirmativeAndNegative);
                if (result == MessageDialogResult.Affirmative)
                {
                    context.TakeHomeToggleEnabled = false;
                    TaskManager.getInstance().ApplyTakeHome(toggleSwitch.IsOn);
                }
                else
                {
                    context.TakeHomeToggleEnabled = true;
                    context.TakeHomeToggleOn = false;
                }
                _taskManagerPauseTokenSource.IsPaused = false;
            }
            
        }
        public async void ShowErrorBox(string errorMessage, string title = "ERROR")
        {
            var errorBox = await context.DialogCoordinator.ShowMessageAsync(context, title,
                errorMessage, MessageDialogStyle.AffirmativeAndNegative,
                new MetroDialogSettings
                {
                    DefaultButtonFocus = MessageDialogResult.Affirmative,
                    AnimateShow = false
                });
            if (errorBox == MessageDialogResult.Affirmative) await Task.Factory.StartNew(Update, TaskCreationOptions.LongRunning);
        }

        public bool VerifySignature(string pathToSig)
        {

            var AutoMuteUsPublicKeyStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("AutopilotQuick.Resources.AutopilotQuick_PubKey.asc");
            using var pgp = new PGP();
            // Verify clear stream
            using var inputFileStream = new FileStream(pathToSig, FileMode.Open);
            return pgp.VerifyClearStream(inputFileStream, AutoMuteUsPublicKeyStream);

        }

        public bool VerifyHashFromSig(string pathToFile, string pathToSignature) //Does not care if the signature is correct or not
        {
            try
            {
                var HashInSig = File.ReadAllLines(pathToSignature).First(x => x.Length == 64); //First line with 64 characters in it
                using var sha256 = SHA256.Create();
                using var fs = new FileStream(pathToFile, FileMode.Open, FileAccess.Read);
                using var bs = new BufferedStream(fs);
                var hash = sha256.ComputeHash(bs);
                var CaptureHashSB = new StringBuilder(2 * hash.Length);
                foreach (var byt in hash) CaptureHashSB.AppendFormat("{0:X2}", byt);

                var CaptureHash = CaptureHashSB.ToString();
                Console.WriteLine($"Got SigHash: {HashInSig}, Downloaded Hash: {CaptureHash}");
                return string.Equals(HashInSig, CaptureHash, StringComparison.CurrentCultureIgnoreCase);
            }
            catch (Exception e)
            {
                return false;
            }
        }

        public async void Update() {
            _taskManagerPauseTokenSource.IsPaused = true;
            var version = new Version();
            var latestVersion = new Version();
            try
            {
                WaitForInternet();
                context.RefreshLatestVersion();
                version = new Version(context.Version);
                latestVersion = new Version(context.LatestVersion);
            }
            catch (Exception e) { _taskManagerPauseTokenSource.IsPaused = false;  }
#if PUBLISH
            var PublicKey = Assembly.GetExecutingAssembly().GetManifestResourceStream("AutopilotQuick.Resources.AutopilotQuick_PubKey.asc");
            int maxStep = 6;
            if (!(latestVersion.CompareTo(version) > 0)) {
                _taskManagerPauseTokenSource.IsPaused = false;
                return;
            }
            var DownloadProgress = await context.DialogCoordinator.ShowProgressAsync(context, $"Step 1/{maxStep} - Downloading", "Percent: 0% (0/0)", isCancelable: false, new MetroDialogSettings { AnimateHide = false });
            DownloadProgress.Maximum = 100;
            using (var client = new WebClient())
            {
                var downloadPath = System.IO.Path.GetTempFileName();
                client.DownloadProgressChanged += (sender, args) =>
                {
                    DownloadProgress.SetProgress(args.ProgressPercentage);
                    DownloadProgress.SetMessage($"Percent: {args.ProgressPercentage}% ({args.BytesReceived.Bytes().Humanize("#.##")}/{args.TotalBytesToReceive.Bytes().Humanize("#.##")})");
                };
                client.DownloadFileCompleted += async (sender, args) =>
                {
                    if (args.Error is not null)
                    {
                        await DownloadProgress.CloseAsync();
                    }
                    else
                    {
                        DownloadProgress.SetTitle($"Step 2/{maxStep} - Downloading signature");
                        using var client2 = new WebClient();
                        var downloadPathSignedHash = System.IO.Path.GetTempFileName();
                        client2.DownloadProgressChanged += (sender, args) =>
                        {
                            DownloadProgress.SetProgress(args.ProgressPercentage);
                            DownloadProgress.SetMessage($"Percent: {args.ProgressPercentage}% ({args.BytesReceived.Bytes().Humanize("#.##")}/{args.TotalBytesToReceive.Bytes().Humanize("#.##")})");
                        };
                        client2.DownloadFileCompleted += async (sender, args) =>
                        {
                            if (args.Error is not null)
                            {
                                await DownloadProgress.CloseAsync();
                                ShowErrorBox(args.Error.Message);
                            }
                            else
                            {
                                DownloadProgress.SetTitle($"Step 3/{maxStep} - Verifying signature");
                                DownloadProgress.SetMessage("");
                                DownloadProgress.SetIndeterminate();
                                bool SignatureValid = VerifySignature(downloadPathSignedHash);
                                if (!SignatureValid)
                                {
                                    await DownloadProgress.CloseAsync();
                                    ShowErrorBox("File signature invalid. If you get this after retrying tell us on discord. It is potentially a security risk.");
                                    return;
                                }

                                DownloadProgress.SetTitle($"Step 4/{maxStep} - Verifying hash");
                                DownloadProgress.SetMessage("");
                                DownloadProgress.SetIndeterminate();
                                bool HashValid = VerifyHashFromSig(downloadPath, downloadPathSignedHash);
                                if (!HashValid)
                                {
                                    await DownloadProgress.CloseAsync();
                                    ShowErrorBox("Capture hash invalid. If you get this after retrying tell us on discord. It is potentially a security risk.");
                                    return;
                                }

                                DownloadProgress.SetTitle($"Step 5/{maxStep} - Extracting");
                                DownloadProgress.SetMessage("Please wait, we may go unresponsive but don't close the window, we will restart the program after.");
                                DownloadProgress.SetIndeterminate();
                                if (!Directory.Exists(System.IO.Path.Join(
                                        Directory.GetParent(Directory.GetParent(App.GetExecutablePath()).FullName).FullName,
                                    "\\AutopilotQuick\\Update")))
                                {
                                    Directory.CreateDirectory(System.IO.Path.Join(
                                        Directory.GetParent(Directory.GetParent(App.GetExecutablePath()).FullName).FullName,
                                        "\\AutopilotQuick\\Update"));
                                }

                                using (ZipArchive archive = ZipFile.OpenRead(downloadPath))
                                {
                                    try
                                    {
                                        var entry = archive.Entries.First(x => x.FullName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
                                        entry.ExtractToFile(Path.Join(Directory.GetParent(Directory.GetParent(App.GetExecutablePath()).FullName).FullName,
                                            "\\AutopilotQuick\\Update", "AutoPilotQuick.exe"), true);
                                    }
                                    catch (Exception e)
                                    {
                                        var errorBox = await context.DialogCoordinator.ShowMessageAsync(context, "ERROR",
                                            e.Message, MessageDialogStyle.AffirmativeAndNegative,
                                            new MetroDialogSettings
                                            {
                                                AffirmativeButtonText = "retry",
                                                NegativeButtonText = "cancel",
                                                DefaultButtonFocus = MessageDialogResult.Affirmative
                                            });
                                        if (errorBox == MessageDialogResult.Affirmative)
                                        {
                                            await Task.Factory.StartNew(Update, TaskCreationOptions.LongRunning);
                                        }
                                    }
                                }


                                //You can't delete a running application. But you can rename it.
                                string appFolder = Path.GetDirectoryName(Environment.ProcessPath);
                                string appName = Path.GetFileNameWithoutExtension(Environment.ProcessPath);
                                string appExtension = Path.GetExtension(Environment.ProcessPath);
                                string archivePath = Path.Combine(appFolder, appName + "_Old" + appExtension);

                                DownloadProgress.SetTitle($"Step 6/{maxStep} - Copying files");
                                DownloadProgress.SetMessage("Finishing up..");
                                File.Move(Process.GetCurrentProcess().MainModule.FileName, archivePath);

                                File.Move(Path.Join(Directory.GetParent(Directory.GetParent(App.GetExecutablePath()).FullName).FullName, "\\AutopilotQuick\\Update", "AutopilotQuick.exe"),
                                    Path.Combine(appFolder, appName + appExtension), true);
                                Application.Current.Invoke(() =>
                                {
                                    Process.Start(Path.Combine(appFolder, appName + appExtension), "/run");
                                    Environment.Exit(0);
                                });
                            }
                        };
                        if (!string.IsNullOrEmpty(context.LatestReleaseAssetSignedHashURL))
                        {
                            var signatureDownloader = client2.DownloadFileTaskAsync(context.LatestReleaseAssetSignedHashURL, downloadPathSignedHash);
                        }
                        else
                        {
                            ShowErrorBox("Release does not have a signature. Not downloading, please retry later.");
                        }

                    }
                };
                var downloaderClient = client.DownloadFileTaskAsync(context.LatestReleaseAssetURL, downloadPath);
            }
#endif
            _taskManagerPauseTokenSource.IsPaused = false;
        }

        private async void ShutdownButton_OnClick(object sender, RoutedEventArgs e)
        {
            _taskManagerPauseTokenSource.IsPaused = true;
            var result = await context.DialogCoordinator.ShowMessageAsync(context, 
                "Shutdown?",
                "Would you like to shutdown this PC?",
                MessageDialogStyle.AffirmativeAndNegative, new MetroDialogSettings(){AffirmativeButtonText = "Shutdown", AnimateHide = true, AnimateShow = true, NegativeButtonText = "Cancel"});
            if (result == MessageDialogResult.Affirmative)
            {
                Process shutdownProcess = new Process();
                shutdownProcess.StartInfo.FileName = "wpeutil";
                shutdownProcess.StartInfo.UseShellExecute = false;
                shutdownProcess.StartInfo.RedirectStandardOutput = true;
                shutdownProcess.StartInfo.CreateNoWindow = true;
                shutdownProcess.StartInfo.Arguments = "shutdown";
                shutdownProcess.Start();
                shutdownProcess.WaitForExit();
            }
            _taskManagerPauseTokenSource.IsPaused = false;
        }

        private async void RestartButton_OnClick(object sender, RoutedEventArgs e)
        {
            _taskManagerPauseTokenSource.IsPaused = true;
            var result = await context.DialogCoordinator.ShowMessageAsync(context,
                "Reboot?",
                "Would you like to reboot this PC?",
                MessageDialogStyle.AffirmativeAndNegative, new MetroDialogSettings() { AffirmativeButtonText = "Reboot", AnimateHide = true, AnimateShow = true, NegativeButtonText = "Cancel" });
            if (result == MessageDialogResult.Affirmative)
            {
                Process shutdownProcess = new Process();
                shutdownProcess.StartInfo.FileName = "wpeutil";
                shutdownProcess.StartInfo.UseShellExecute = false;
                shutdownProcess.StartInfo.RedirectStandardOutput = true;
                shutdownProcess.StartInfo.CreateNoWindow = true;
                shutdownProcess.StartInfo.Arguments = "reboot";
                shutdownProcess.Start();
                shutdownProcess.WaitForExit();
            }
            _taskManagerPauseTokenSource.IsPaused = false;
        }
    }
}
