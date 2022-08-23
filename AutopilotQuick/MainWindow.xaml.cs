using MahApps.Metro.Controls.Dialogs;
using NLog;
using System;
using System.Collections.Generic;
using System.ComponentModel;
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
using System.Threading;
using System.Windows.Forms;
using System.Windows.Media.Animation;
using AutopilotQuick.LogMan;
using ControlzEx.Theming;
using MahApps.Metro.Controls;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;
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
        private readonly CancellationTokenSource _cancelTokenSource = new();
        private List<Task> _backgroundTasks = new List<Task>();
        private bool Updating = false;
        
        public MainWindow()
        {
            InitializeComponent();
            context = new UserDataContext(DialogCoordinator.Instance, this);
            DataContext = context;
            WimMan.getInstance().SetContext(context);
            context.PropertyChanged += ContextOnPropertyChanged;
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

            if (!TaskManager.getInstance().Enabled)
            {
                ResizeMode = ResizeMode.CanResize;
                WindowState = WindowState.Normal;
                ShowCloseButton = true;
            }
            else
            {
                ResizeMode = ResizeMode.NoResize;
                WindowState = WindowState.Maximized;
                ShowCloseButton = false;
            }

        }

        private void ContextOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "DeveloperModeEnabled")
            {
                if (context.DeveloperModeEnabled)
                {
                    ThemeManager.Current.ThemeSyncMode = ThemeSyncMode.DoNotSync;

                    string BaseColor = ThemeManager.BaseColorDark;

                    var newTheme2 = new Theme("CustomTheme",
                        "CustomTheme",
                        BaseColor,
                        "CustomAccent",
                        System.Windows.Media.Color.FromArgb(255,229,20,0),
                        new SolidColorBrush(System.Windows.Media.Color.FromArgb(255,140,0,0)),
                        true,
                        false);
                    ThemeManager.Current.ChangeTheme(this, newTheme2);
                }
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

        private async void MetroWindow_Loaded(object sender, RoutedEventArgs e)
        {
            var cancellationToken = _cancelTokenSource.Token;
            
            Application.Current.Exit += (o, args) =>
            {
                _cancelTokenSource.Cancel();
                Close();
            };


            BatteryMan.getInstance().BatteryUpdated += MainWindow_BatteryUpdated;

            
            TaskManager.getInstance().TotalTaskProgressChanged += MainWindow_TotalTaskProgressChanged;
            TaskManager.getInstance().CurrentTaskProgressChanged += MainWindow_CurrentTaskProgressChanged;
            TaskManager.getInstance().CurrentTaskMessageChanged += MainWindow_CurrentTaskMessageChanged;
            TaskManager.getInstance().CurrentTaskNameChanged += MainWindow_CurrentTaskNameChanged;
            
            
            InternetMan.getInstance().InternetBecameAvailable += MainWindow_InternetBecameAvailable;
            DurableAzureBackgroundTask.getInstance().StartTimer(context);
            BatteryMan.getInstance().StartTimer();
            InternetMan.getInstance().StartTimer();
            var TaskManagerTask = Task.Run(() => TaskManager.getInstance().Run(context, _taskManagerPauseTokenSource.Token), cancellationToken);

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
            Task.Factory.StartNew(UpdateWithPause, TaskCreationOptions.LongRunning);
        }

        private async void UpdateWithPause()
        {
            if (Updating) return;
            using var t = App.telemetryClient.StartOperation<RequestTelemetry>("Checking for updates");
            Updating = true;
            Logger.Info("Pausing for update checking&applying");
            _taskManagerPauseTokenSource.IsPaused = true;
            try
            {
                Logger.Info("Updating");
                await Update();
                Logger.Info("Done updating");
            }
            finally
            {
                Logger.Info("unpausing done with update checking&applying");
                _taskManagerPauseTokenSource.IsPaused = false;
            }
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

        public async Task Update()
        {
            var version = new Version();
            var latestVersion = new Version();
            try
            {
                WaitForInternet();
                context.RefreshLatestVersion();
                version = new Version(context.Version);
                latestVersion = new Version(context.LatestVersion);
            }
            catch (Exception e)
            {
                Logger.Error(e);
            }
#if PUBLISH
            var PublicKey = Assembly.GetExecutingAssembly().GetManifestResourceStream("AutopilotQuick.Resources.AutopilotQuick_PubKey.asc");
            int maxStep = 6;
            if (!(latestVersion.CompareTo(version) > 0)) {
                return;
            }
            
            var DownloadProgress = await context.DialogCoordinator.ShowProgressAsync(context, $"Step 1/{maxStep} - Downloading", "Percent: 0% (0/0)", isCancelable: false, new MetroDialogSettings { AnimateHide = false });
            DownloadProgress.Maximum = 100;
            
            var downloadPath = System.IO.Path.GetTempFileName();

            try
            {
                using var DownloadClient = new HttpClientDownloadWithProgress(context.LatestReleaseAssetURL, downloadPath);
                DownloadClient.ProgressChanged += (totalFileSize, totalBytesDownloaded, progressPercentage) =>
                {
                    if (!progressPercentage.HasValue) return;
                    DownloadProgress.SetProgress(progressPercentage.Value);
                    DownloadProgress.SetMessage(
                        $"Percent: {progressPercentage}% ({totalBytesDownloaded.Bytes().Humanize("#.##")}/{totalFileSize.Value.Bytes().Humanize("#.##")})");
                };
                await DownloadClient.StartDownload();
            }
            catch (Exception e)
            {
                await DownloadProgress.CloseAsync();
                ShowErrorBox(e.Message);
            }
            
            var downloadPathSignedHash = System.IO.Path.GetTempFileName();
            try
            {
                DownloadProgress.SetTitle($"Step 2/{maxStep} - Downloading signature");
                using var SignatureDownloadClient =
                    new HttpClientDownloadWithProgress(context.LatestReleaseAssetSignedHashURL, downloadPathSignedHash);
                SignatureDownloadClient.ProgressChanged += (totalFileSize, totalBytesDownloaded, progressPercentage) =>
                {
                    if (!progressPercentage.HasValue) return;
                    DownloadProgress.SetProgress(progressPercentage.Value);
                    DownloadProgress.SetMessage(
                        $"Percent: {progressPercentage}% ({totalBytesDownloaded.Bytes().Humanize("#.##")}/{totalFileSize.Value.Bytes().Humanize("#.##")})");
                };
                await SignatureDownloadClient.StartDownload();
            }
            catch (Exception e)
            {
                await DownloadProgress.CloseAsync();
                ShowErrorBox(e.Message);
            }
            
            //We have the zip file downloaded to downloadPath, and the Signature downloaded to downloadPathSignedHash
            //Now we need to verify the signature of the hash
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
            
            //Now we verify the signed hash 
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
            
            //Hash and signature valid. Now we need to extract
            DownloadProgress.SetTitle($"Step 5/{maxStep} - Extracting");
            DownloadProgress.SetMessage("Please wait, we may go unresponsive but don't close the window, we will restart the program after.");
            DownloadProgress.SetIndeterminate();
            var UpdateFolder =
                System.IO.Path.Join(Directory.GetParent(Directory.GetParent(App.GetExecutablePath()).FullName).FullName,
                    "\\AutopilotQuick\\Update");
            //Clear out the update folder
            if (Directory.Exists(UpdateFolder))
            {
                Directory.Delete(UpdateFolder, true);
            }
            Directory.CreateDirectory(UpdateFolder);
            
            //Extract the zip archive
            using (ZipArchive archive = ZipFile.OpenRead(downloadPath))
            {
                try
                {
                    foreach (var thing in archive.Entries)
                    {
                        thing.ExtractToFile(Path.Join(UpdateFolder, thing.FullName), true);
                    }
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
            
            Application.Current.Invoke(() =>
            {
                
                var psscriptPath = Path.Join(UpdateFolder, $"script-{Guid.NewGuid()}.ps1");
                var files = Assembly.GetExecutingAssembly().GetManifestResourceNames();
                foreach (var fileName in files.Where(x => x.EndsWith("UpdateScript.ps1")))
                {
                    using (var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream(fileName))
                    {
                        using (var file = new FileStream(psscriptPath, FileMode.Create, FileAccess.Write))
                        {
                            resource.CopyTo(file);
                        }
                    }
                }

                var script = $"$AutopilotQuickPath = \"{Directory.GetParent(UpdateFolder)}\"\n"+File.ReadAllText(psscriptPath);
                
                File.WriteAllText(psscriptPath, script);
                
                Process updateProcess = new Process();
                updateProcess.StartInfo.FileName = "Powershell.exe";
                updateProcess.StartInfo.UseShellExecute = true;
                updateProcess.StartInfo.RedirectStandardOutput = false;
                updateProcess.StartInfo.CreateNoWindow = false;
                updateProcess.StartInfo.Arguments = psscriptPath;
                updateProcess.Start();
                Thread.Sleep(1000);
                Environment.Exit(0);
            });
#endif
            
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

        private bool runningAlready = false;
        private void F10KeyPressed_OnExecuted(object? sender, object e)
        {
            if (!runningAlready)
            {
                this.BeginStoryboard(RainbowStoryBoard);
                runningAlready = true;
            }
            
        }

        private async void F7KeyPressed_OnExecuted(object? sender, object e)
        {
            Dispatcher.BeginInvoke(async () =>
            {
                var debugWindow = new DebugWindow(DeviceID.DeviceIdentifierMan.getInstance().GetDeviceIdentifier(), App.SessionID, $"{App.GetVersion().FileMajorPart}.{App.GetVersion().FileMinorPart}.{App.GetVersion().FileBuildPart}");
                debugWindow.Show();
            });

        }
    }
}
