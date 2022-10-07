using MahApps.Metro.Controls.Dialogs;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Humanizer;
using System.IO;
using System.IO.Compression;
using System.Diagnostics;
using PgpCore;
using System.Security.Cryptography;
using System.Threading;
using System.Windows.Threading;
using AQ.GroupManagementLibrary;
using AutopilotQuick.Banshee;
using AutopilotQuick.CookieEgg;
using AutopilotQuick.KeyboardTester;
using AutopilotQuick.LogMan;
using AutopilotQuick.SnakeGame;
using AutopilotQuick.WMI;
using ControlzEx.Theming;
using Humanizer.Localisation;
using MahApps.Metro.Controls;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Nito.AsyncEx;
using ORMi;
using Application = System.Windows.Application;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using NHotkey.Wpf;

namespace AutopilotQuick
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow
    {
        private static readonly ILogger<MainWindow> Logger = App.GetLogger<MainWindow>();
        public UserDataContext context;
        private readonly bool Updated;
        private readonly PauseTokenSource _taskManagerPauseTokenSource = new ();
        private readonly CancellationTokenSource _cancelTokenSource = new();
        private bool Updating = false;

        public MainWindow()
        {
            this.Dispatcher.UnhandledException += (sender, args) => App.GetLogger<App>().LogError(args.Exception, "Unhandled exception: {e}", args.Exception);
            InitializeComponent();
            context = new UserDataContext(DialogCoordinator.Instance, this);
            DataContext = context;
            WimMan.getInstance().SetContext(context);
            context.PropertyChanged += ContextOnPropertyChanged;
            
            if (!TaskManager.GetInstance().Enabled)
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
            var timer = new DispatcherTimer
            {
                Interval = 1.Seconds()
            };
            timer.Tick += Timer_Tick;
            timer.Start();
        }
        
        private void Timer_Tick(object? sender, EventArgs e)
        {
            context.SetCurrentTime(DateTime.Now);
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

        private void UnregisterConflictingKeybinds()
        {
            var keys = new List<string>()
            {
                "RainbowMode",
                "PlayPortalMusic",
                "DebugMenu",
                "IncVolume",
                "DecVolume"
            };
            foreach (var key in keys)
            {
                try
                {
                    HotkeyManager.Current.Remove(key);
                }
                catch (Exception e)
                {
                    Logger.LogError(e, "Failed to de-register hotkey: {key}, Error: {e}",key, e);
                }
                
            }
            
        }

        private void RegisterConflictingKeybinds()
        {
            HotkeyManager.Current.AddOrReplace("RainbowMode", Key.F10, ModifierKeys.None, true, F10KeyPressed_OnExecuted);
            HotkeyManager.Current.AddOrReplace("DebugMenu", Key.F7, ModifierKeys.None, true, F7KeyPressed_OnExecuted);
            HotkeyManager.Current.AddOrReplace("IncVolume", Key.OemPlus, ModifierKeys.None, false, (o, args) =>
            {
                BansheePlayer.GetInstance().IncVolume();
            });
            HotkeyManager.Current.AddOrReplace("DecVolume", Key.OemMinus, ModifierKeys.None, false, (o, args) =>
            {
                BansheePlayer.GetInstance().DecVolume();
            });
            
        }

        private KeyboardWindow? _keyboardWindow = null;
        
        private async void MetroWindow_Loaded(object sender, RoutedEventArgs e)
        {
            
            var cancellationToken = _cancelTokenSource.Token;
            
            Application.Current.Exit += (o, args) =>
            {
                _cancelTokenSource.Cancel();
                Close();
            };

            
            _ = Dispatcher.BeginInvoke(() =>
                {
                    
                    HotkeyManager.Current.AddOrReplace("OpenKeyboardTest", Key.K, ModifierKeys.Control, true,
                        (o, args) =>
                        {
                            _keyboardWindow ??= new KeyboardWindow();
                            _keyboardWindow.Closed += (o, args) =>
                            {
                                context.KeyboardTestEnabled = false;
                                RegisterConflictingKeybinds();
                                _keyboardWindow = null;
                            };
                            context.KeyboardTestEnabled = true;
                            UnregisterConflictingKeybinds();
                            _keyboardWindow.Show();
                        });
                    
                    HotkeyManager.Current.AddOrReplace("HotkeyMenu", Key.H, ModifierKeys.Control, true, (o, args) =>
                    {
                        this.Dispatcher.Invoke(() =>
                        {
                            this.ToggleFlyout(0);
                        });
                    });
                    HotkeyManager.Current.AddOrReplace("Snake", Key.N, ModifierKeys.Control, true, (o, args) =>
                    {
                        if (_snakeWindow is null)
                        {
                            _snakeWindow = new SnakeWindow();
                            _snakeWindow.Closed += (sender1, eventArgs) => { _snakeWindow = null; };
                            _snakeWindow.Show();
                        }
                    });
                    HotkeyManager.Current.AddOrReplace("ToggleTakeHome", Key.T, ModifierKeys.Control, true, (o, args) =>
                    {
                        if (TakeHomeToggle.IsEnabled)
                        {
                            context.TakeHomeToggleOn = true;
                        }
                    });
                    HotkeyManager.Current.AddOrReplace("OpenPowershell", Key.F10, ModifierKeys.Shift, true, (o, args) =>
                    {
                        LaunchPowershell();
                    });
                    HotkeyManager.Current.AddOrReplace("ToggleSharedPC", Key.S, ModifierKeys.Control, true, (o, args) =>
                    {
                        if (!context.SharedPCCheckboxEnabled) return;

                        context.SharedPCChecked = context.SharedPCChecked is not (null or true);

                        SharedPCSwitch_OnCheckedOrUncheck(this, new RoutedEventArgs());
                    });
                    HotkeyManager.Current.AddOrReplace("OpenPowerMenu", Key.P, ModifierKeys.Control,
                        true, (o, args) => { ShutdownButton_OnClick(this, new RoutedEventArgs()); });
                    
                    HotkeyManager.Current.AddOrReplace("PlayPauseElevatorMusic", Key.M, ModifierKeys.Control, true, (o, args) =>
                    {
                        if (!BansheePlayer.GetInstance().IsPlaying())
                        {
                            Task.Run(()=>BansheePlayer.GetInstance().Play(context));
                        }
                        else
                        {
                            BansheePlayer.GetInstance().Stop();
                        }
                    });
                    RegisterConflictingKeybinds();
                }, DispatcherPriority.Normal
            );
            KeyDown += WinOnKeyDown;
            

            //BatteryMan.GetInstance().BatteryUpdated += MainWindow_BatteryUpdated;

            
            TaskManager.GetInstance().TotalTaskProgressChanged += MainWindow_TotalTaskProgressChanged;
            TaskManager.GetInstance().CurrentTaskProgressChanged += MainWindow_CurrentTaskProgressChanged;
            TaskManager.GetInstance().CurrentTaskMessageChanged += MainWindow_CurrentTaskMessageChanged;
            TaskManager.GetInstance().CurrentTaskNameChanged += MainWindow_CurrentTaskNameChanged;
            
            
            InternetMan.GetInstance().InternetBecameAvailable += MainWindow_InternetBecameAvailable;
            InternetMan.GetInstance().InternetBecameUnavailable += ((o, args) => this.Dispatcher.Invoke(()=>context.ConnectedToInternet = InternetConnectionStatus.Disconnected));
            await Task.Run(() => HeadphoneMan.GetInstance().StartTimer(context), cancellationToken);
            await Task.Run(() => DurableAzureBackgroundTask.getInstance().StartTimer(context), cancellationToken);
            //await Task.Run(() =>BatteryMan.GetInstance().StartTimer(), cancellationToken);
            await Task.Run(() =>InternetMan.GetInstance().StartTimer(context), cancellationToken);
            var TaskManagerTask = Task.Factory.StartNew(async ()=>await TaskManager.GetInstance().Run(context, _taskManagerPauseTokenSource.Token),cancellationToken, TaskCreationOptions.LongRunning, TaskScheduler.Default);

        }

        private CookieWindow? _cookieWindow = null;
        private SnakeWindow? _snakeWindow = null;
        private DateTime LastCookieTime = DateTime.MinValue;
        private int msHeldCookieDelay = 100;
        private void WinOnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.C && ((e.IsRepeat && (DateTime.UtcNow - LastCookieTime).TotalMilliseconds >= msHeldCookieDelay) || !e.IsRepeat))
            {
                if (_cookieWindow is null)
                {
                    _cookieWindow = new CookieWindow();
                    _cookieWindow.Show();
                }
                _cookieWindow.AddCookie();
                LastCookieTime = DateTime.UtcNow;
            }
        }

        private static void LaunchPowershell()
        {
            var powerShellProcess = new Process();
            powerShellProcess.StartInfo.FileName = "Powershell.exe";
            powerShellProcess.StartInfo.UseShellExecute = true;
            powerShellProcess.StartInfo.CreateNoWindow = false;
            powerShellProcess.Start();
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

        private bool AlreadyRanUpdater = false;
        private void MainWindow_InternetBecameAvailable(object? sender, EventArgs e)
        {
            
            context.ConnectedToInternet = InternetConnectionStatus.Connected;
            if (!AlreadyRanUpdater)
            {
                AlreadyRanUpdater = true;
                Task.Factory.StartNew(UpdateSharedPCBoxWhenInternet, TaskCreationOptions.LongRunning);
                Task.Factory.StartNew(UpdateWithPause, TaskCreationOptions.LongRunning);
            }
            
        }

        private async void UpdateWithPause()
        {
            if (Updating) return;
            using var t = App.telemetryClient.StartOperation<RequestTelemetry>("Checking for updates");
            Updating = true;
            Logger.LogInformation("Pausing for update checking&applying");
            _taskManagerPauseTokenSource.IsPaused = true;
            try
            {
                Logger.LogInformation("Updating");
                await Update();
                Logger.LogInformation("Done updating");
            }
            finally
            {
                Logger.LogInformation("unpausing done with update checking&applying");
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
                await InternetMan.WaitForInternetAsync(context);
                context.RefreshLatestVersion();
                version = new Version(context.Version);
                latestVersion = new Version(context.LatestVersion);
            }
            catch (Exception e)
            {
                Logger.LogError(e, "Got error {e}", e);
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
                DateTime LastUpdate = DateTime.MinValue;
                DownloadClient.ProgressChanged += (sender, args) =>
                {
                    var now = DateTime.UtcNow;
                    if ((now - LastUpdate).TotalMilliseconds >= 50)
                    {
                        LastUpdate = now;
                        var eta = "calculating...";
                        if (args.AverageBytesPerSecondSpeed > 0)
                        {
                            eta = ((args.TotalBytesToReceive - args.ReceivedBytesSize) /
                                   args.AverageBytesPerSecondSpeed)
                                .Seconds().Humanize(minUnit: TimeUnit.Second, precision: 2);
                        }

                        const int space = 4;
                        var info = new List<KeyValuePair<string, string>>()
                        {
                            new("Time left:", eta),
                            new("Transferred:",
                                $"{args.ReceivedBytesSize.Bytes().Humanize("#.00")} of {args.TotalBytesToReceive.Bytes().Humanize("#.00")}"),
                            new("Speed:",
                                $"{args.BytesPerSecondSpeed.Bytes().Per(1.Seconds()).Humanize("#")} (avg: {args.AverageBytesPerSecondSpeed.Bytes().Per(1.Seconds()).Humanize("#")})")
                        };
                        var longest = info.MaxBy(x => x.Key.Length).Key.Length;
                        var maxLength = longest + space;
                        var sb = new StringBuilder();
                        foreach (var pair in info)
                        {
                            var newKey = pair.Key.PadRight(maxLength + 2);

                            if (pair.Key.Length == longest)
                            {
                                newKey = pair.Key.PadRight(maxLength);
                            }

                            sb.AppendLine($"{newKey} {pair.Value}");
                        }

                        DownloadProgress.SetProgress(args.ProgressPercentage);
                        DownloadProgress.SetMessage(sb.ToString());
                    }
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
                DateTime LastUpdate = DateTime.MinValue;
                SignatureDownloadClient.ProgressChanged += (sender,args) =>
                {
                    var now = DateTime.UtcNow;
                    if ((now - LastUpdate).TotalMilliseconds >= 50)
                    {
                        LastUpdate = now;
                        var eta = "calculating...";
                        if (args.AverageBytesPerSecondSpeed > 0)
                        {
                            eta = ((args.TotalBytesToReceive - args.ReceivedBytesSize) /
                                   args.AverageBytesPerSecondSpeed)
                                .Seconds().Humanize(minUnit: TimeUnit.Second, precision: 2);
                        }

                        const int space = 4;
                        var info = new List<KeyValuePair<string, string>>()
                        {
                            new("Time left:", eta),
                            new("Transferred:",
                                $"{args.ReceivedBytesSize.Bytes().Humanize("#.00")} of {args.TotalBytesToReceive.Bytes().Humanize("#.00")}"),
                            new("Speed:",
                                $"{args.BytesPerSecondSpeed.Bytes().Per(1.Seconds()).Humanize("#")} (avg: {args.AverageBytesPerSecondSpeed.Bytes().Per(1.Seconds()).Humanize("#")})")
                        };
                        var longest = info.MaxBy(x => x.Key.Length).Key.Length;
                        var maxLength = longest + space;
                        var sb = new StringBuilder();
                        foreach (var pair in info)
                        {
                            var newKey = pair.Key.PadRight(maxLength + 2);

                            if (pair.Key.Length == longest)
                            {
                                newKey = pair.Key.PadRight(maxLength);
                            }

                            sb.AppendLine($"{newKey} {pair.Value}");
                        }

                        DownloadProgress.SetProgress(args.ProgressPercentage);
                        DownloadProgress.SetMessage(sb.ToString());
                    }
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
                "Power options",
                "What would you like to do?",
                MessageDialogStyle.AffirmativeAndNegativeAndSingleAuxiliary, new MetroDialogSettings(){
                    AffirmativeButtonText = "Shutdown",
                    FirstAuxiliaryButtonText = "Reboot",
                    AnimateHide = true,
                    AnimateShow = true,
                    NegativeButtonText = "Cancel"});
            
            Process shutdownProcess = new Process();
            shutdownProcess.StartInfo.FileName = "wpeutil";
            shutdownProcess.StartInfo.UseShellExecute = false;
            shutdownProcess.StartInfo.RedirectStandardOutput = true;
            shutdownProcess.StartInfo.CreateNoWindow = true;
            
            if (result == MessageDialogResult.Affirmative)
            {
                var p = await context.DialogCoordinator.ShowProgressAsync(context, "Shutting down...", "", false);
                p.SetIndeterminate();
                shutdownProcess.StartInfo.Arguments = "shutdown";
                shutdownProcess.Start();
                await shutdownProcess.WaitForExitAsync();
            } else if (result == MessageDialogResult.FirstAuxiliary)
            {
                var p = await context.DialogCoordinator.ShowProgressAsync(context, "Rebooting...", "", false);
                p.SetIndeterminate();
                shutdownProcess.StartInfo.Arguments = "reboot";
                shutdownProcess.Start();
                await shutdownProcess.WaitForExitAsync();
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

        private  void F7KeyPressed_OnExecuted(object? sender, object e)
        {
            var debugWindow = new DebugWindow();
            debugWindow.Show();
        }

        public record struct GroupManConfig(string APIKEY, string URL);
        public async void UpdateSharedPCBoxWhenInternet()
        {
            var groupManConfigCache = new Cacher(CachedResourceUris.GroupManConfig, context);
            if (!groupManConfigCache.IsUpToDate || !groupManConfigCache.FileCached)
            {
                await Task.Run(async () => await groupManConfigCache.DownloadUpdateAsync());
            }

            GroupManConfig config =
                JsonConvert.DeserializeObject<GroupManConfig>(
                    await File.ReadAllTextAsync(groupManConfigCache.FilePath));
            var client =
                new GroupManagementClient(App.GetLogger<GroupManagementClient>(), config.APIKEY, config.URL);

            WMIHelper helper = new WMIHelper("root\\CimV2");
            string serviceTag = helper.QueryFirstOrDefault<Bios>().SerialNumber;

            var ismemeber = await client.IsSharedPCMember(serviceTag);
            if (!ismemeber.HasValue) //Got error, which means we should probably turn off the checkbox
            {
                this.Dispatcher.Invoke(() =>
                {
                    context.SharedPCCheckboxEnabled = false;
                    context.UserRequestedChangeSharedPC = false;
                    context.SharedPCChecked = null;
                });
                return;
            }

            if (!context.UserRequestedChangeSharedPC)
            {
                this.Dispatcher.Invoke(() =>
                {
                    context.SharedPCChecked = ismemeber.Value.TransitiveMemberInGroup;
                    if (ismemeber.Value.TransitiveMemberInGroup && !ismemeber.Value.DirectMemberInGroup)
                    {
                        context.SharedPCCheckboxEnabled =
                            false; //Disable check box because we are not direct members, only transitive
                    }
                });
                return;
            }
            else if (context.UserRequestedChangeSharedPC &&
                     context.SharedPCChecked == ismemeber.Value.TransitiveMemberInGroup)
            {
                context.UserRequestedChangeSharedPC = false;
            }
        }
        

        private void ToggleFlyout(int index)
        {
            var flyout = this.Flyouts?.Items[index] as Flyout;
            if (flyout == null)
            {
                return;
            }

            flyout.IsOpen = !flyout.IsOpen;
        }

        private void HotKeyFlyoutButton_Click(object sender, RoutedEventArgs e)
        {
            this.ToggleFlyout(0);
        }

        private int count = 0;
        private async void SharedPCSwitch_OnCheckedOrUncheck(object sender, RoutedEventArgs e)
        {
            if (context.SharedPCChecked is null)
            {
                context.SharedPCChecked = false;
            }
            context.UserRequestedChangeSharedPC = true; //User pressed the button so we need to flip this,
            count++;
        }
    }
}
