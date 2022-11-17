using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows.Input;
using AutopilotQuick.Annotations;
using AutopilotQuick.DeviceID;
using Humanizer;
using MahApps.Metro.Controls;
using MahApps.Metro.Controls.Dialogs;
using NLog;
using Notification.Wpf;
using Octokit;
using Polly;
using Polly.Retry;

namespace AutopilotQuick
{
    public class UserDataContext : INotifyPropertyChanged
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        public IDialogCoordinator DialogCoordinator { get; set; }

        public bool UserRequestedChangeSharedPC { get; set; } = false;

        public string Title { get; set; } = "";
        public string Version { get; set; }
        public string LatestVersion { get; set; }
        public string LatestReleaseAssetURL { get; set; }
        public string LatestReleaseAssetSignedHashURL { get; set; }
        
        public NotificationManager NotifcationManager = new NotificationManager();

        private InternetConnectionStatus _connectedToInternet { get; set; } = InternetConnectionStatus.Loading;
        

        public InternetConnectionStatus ConnectedToInternet { get { return _connectedToInternet; } set { _connectedToInternet = value; OnPropertyChanged(nameof(ConnectedToInternet)); } }


        private double _totalProgress = 0;
        public double TotalProgress
        {
            get { return _totalProgress; }
            set { _totalProgress = value; OnPropertyChanged(nameof(TotalProgress)); }
        }

        private bool _isCharging = false;
        public bool IsCharging
        {
            get { return _isCharging; }
            set { _isCharging = value; OnPropertyChanged(nameof(IsCharging)); }
        }

        private int _batteryPercent = 0;
        public int BatteryPercent
        {
            get { return _batteryPercent; }
            set { _batteryPercent = value; OnPropertyChanged(nameof(BatteryPercent)); }
        }


        private bool _takeHomeToggleEnabled = true;
        public bool TakeHomeToggleEnabled
        {
            get { return _takeHomeToggleEnabled; }
            set { _takeHomeToggleEnabled = value; OnPropertyChanged(nameof(TakeHomeToggleEnabled)); }
        }

        private bool _takeHomeToggleOn = false;
        public bool TakeHomeToggleOn
        {
            get { return _takeHomeToggleOn; }
            set { _takeHomeToggleOn = value; OnPropertyChanged(nameof(TakeHomeToggleOn)); }
        }

        public double _currentStepProgress = 0;
        public double CurrentStepProgress
        {
            get { return _currentStepProgress; }
            set { _currentStepProgress = value; OnPropertyChanged(nameof(CurrentStepProgress)); }
        }

        public bool _currentStepIndeterminate = true;
        public bool CurrentStepIndeterminate
        {
            get { return _currentStepIndeterminate; }
            set { _currentStepIndeterminate = value; OnPropertyChanged(nameof(CurrentStepIndeterminate)); }
        }

        public bool _totalStepIndeterminate = true;
        public bool TotalStepIndeterminate
        {
            get { return _totalStepIndeterminate; }
            set { _totalStepIndeterminate = value; OnPropertyChanged(nameof(TotalStepIndeterminate)); }
        }
        public string _totalStepMessage = "Starting up...";
        public string TotalStepMessage
        {
            get { return _totalStepMessage; }
            set { _totalStepMessage = value; OnPropertyChanged(nameof(TotalStepMessage)); }
        }

        private string _currentStepName = "Starting up...";
        public string CurrentStepName
        {
            get { return _currentStepName; }
            set { _currentStepName = value; OnPropertyChanged(nameof(CurrentStepName)); }
        }

        private string _currentStepMessage = "Please wait for the application to finish starting up";
        public string CurrentStepMessage
        {
            get { return _currentStepMessage; }
            set { _currentStepMessage = value; OnPropertyChanged(nameof(CurrentStepMessage)); }
        }

        private bool _developerModeEnabled = false;
        private bool? _sharedPcChecked;
        private bool _sharedPcCheckboxEnabled = true;

        public bool DeveloperModeEnabled
        {
            get { return _developerModeEnabled; }
            set { _developerModeEnabled = value; OnPropertyChanged(nameof(DeveloperModeEnabled)); }
        }

        public bool KeyboardTestEnabled = false;
        public ICommand EnableDeveloperModeCmd => new SimpleCommand
        {
            CanExecuteDelegate = x => !DeveloperModeEnabled && !KeyboardTestEnabled,
            ExecuteDelegate = async x =>
            {
                DeveloperModeEnabled = true;
                await DialogCoordinator.ShowMessageAsync(this, "Developer mode",
                    "Developer mode was enabled by pressing F1.\n" +
                    "The Machine will not reboot automatically at the end of the task sequence, you can press the reboot button when you are done.");
            }
        };

        public bool SharedPCCheckboxEnabled
        {
            get => _sharedPcCheckboxEnabled;
            set
            {
                if (value == _sharedPcCheckboxEnabled) return;
                _sharedPcCheckboxEnabled = value;
                OnPropertyChanged();
            }
        }

        public bool? SharedPCChecked
        {
            get => _sharedPcChecked;
            set
            {
                if (value == _sharedPcChecked) return;
                _sharedPcChecked = value;
                OnPropertyChanged();
            }
        }

        public HeadphoneState HeadphonesActive
        {
            get => _headphonesActive;
            set
            {
                if (value == _headphonesActive) return;
                _headphonesActive = value;
                OnPropertyChanged();
            }
        }

        public bool Playing
        {
            get => _playing;
            set
            {
                if (value == _playing) return;
                _playing = value;
                OnPropertyChanged();
            }
        }

        public record struct HotkeyListItem(string Name, HotKey HotKey, HotkeyType HotkeyType);


        private List<HotkeyListItem> _hotkeyList = new List<HotkeyListItem>()
        {
            new HotkeyListItem("Enable Developer mode", new HotKey(Key.F1), HotkeyType.Normal),
            new HotkeyListItem("Open debug menu", new HotKey(Key.F7), HotkeyType.Normal),
            new HotkeyListItem("Toggle hotkey menu", new HotKey(Key.H, ModifierKeys.Control), HotkeyType.Normal),
            new HotkeyListItem("Enable takehome", new HotKey(Key.T, ModifierKeys.Control), HotkeyType.Normal),
            new HotkeyListItem("Open power menu", new HotKey(Key.P, ModifierKeys.Control), HotkeyType.Normal),
            new HotkeyListItem("Toggle SharedPC", new HotKey(Key.S, ModifierKeys.Control), HotkeyType.Normal),
            new HotkeyListItem("Launch powershell", new HotKey(Key.F10, ModifierKeys.Shift), HotkeyType.Normal),
            new HotkeyListItem("Launch keyboard test", new HotKey(Key.K, ModifierKeys.Control), HotkeyType.Normal),
            new HotkeyListItem("Enable Rainbow mode", new HotKey(Key.F10), HotkeyType.EasterEgg),
            new HotkeyListItem("Make cookies rain down", new HotKey(Key.C), HotkeyType.EasterEgg),
            new HotkeyListItem("Play snake", new HotKey(Key.N, ModifierKeys.Control), HotkeyType.EasterEgg),
            new HotkeyListItem("Play/Pause music", new HotKey(Key.M, ModifierKeys.Control), HotkeyType.EasterEgg),
            new HotkeyListItem("Increase music volume", new HotKey(Key.OemPlus), HotkeyType.EasterEgg),
            new HotkeyListItem("Decrease music volume", new HotKey(Key.OemMinus), HotkeyType.EasterEgg),
        };

        private string _currentTime;
        private HeadphoneState _headphonesActive = HeadphoneState.NotFound;
        private bool _playing = false;
        

        public IEnumerable<HotkeyListItem> NormalHotkeyList
        {
            get
            {
                return _hotkeyList.Where(x => x.HotkeyType == HotkeyType.Normal);
            }
        }

        public IEnumerable<HotkeyListItem> EggHotkeyList
        {
            get {
                return _hotkeyList.Where(x=>x.HotkeyType == HotkeyType.EasterEgg);
            }
        }

        public string CurrentTime
        {
            get => _currentTime;
            set
            {
                if (value.Equals(_currentTime)) return;
                _currentTime = value;
                OnPropertyChanged();
            }
        }

        public void SetCurrentTime(DateTime currentTime)
        {
            CurrentTime = currentTime.ToString("MM/dd/yyyy hh:mm:ss tt");
        }

        public UserDataContext(IDialogCoordinator dialogCoordinator, MainWindow window)
        {
            MainWindow = window;
            DialogCoordinator = dialogCoordinator;
            FileVersionInfo v = FileVersionInfo.GetVersionInfo(App.GetExecutablePath());
            Version = $"{v.FileMajorPart}.{v.FileMinorPart}.{v.FileBuildPart}";
            OnPropertyChanged(nameof(Version));
            Title = $" - {Version} | ID: {DeviceIdentifierMan.getInstance().GetDeviceIdentifier()}";
            OnPropertyChanged(nameof(Title));
            
            SetCurrentTime(DateTime.Now);
            
        }

        public MainWindow MainWindow { get; }
        public void RefreshLatestVersion()
        {
            while (!InternetMan.GetInstance().IsConnected)
            {
                Thread.Sleep(200);
            }
            try
            {
                RetryPolicy retry = Policy.Handle<AggregateException>().WaitAndRetry(5, retryAttempt => 5.Seconds());
                
                GitHubClient client = new GitHubClient(new ProductHeaderValue("AutopilotQuick", Version));
                Release latest = new Release();
                var result = retry.ExecuteAndCapture<Release>(() => client.Repository.Release.GetLatest("PCSD202", "AutopilotQuick").Result);
                if (result.Outcome == OutcomeType.Successful)
                {
                    latest = result.Result;
                    LatestReleaseAssetURL = latest.Assets.First(x => x.Name == "AutopilotQuick.zip").BrowserDownloadUrl;
                    if (latest.Assets.Any(x => x.Name == "AutopilotQuick.zip.sha256.pgp"))
                        LatestReleaseAssetSignedHashURL = latest.Assets.First(x => x.Name == "AutopilotQuick.zip.sha256.pgp").BrowserDownloadUrl;

                    LatestVersion = $"{latest.TagName}";
                    OnPropertyChanged(nameof(LatestVersion));
                    OnPropertyChanged(nameof(LatestReleaseAssetSignedHashURL));
                    OnPropertyChanged(nameof(LatestReleaseAssetURL));
                }
                else
                {
                    throw result.FinalException;
                }
            }
            catch (Exception e)
            {
                Logger.Error(e.ToString());
                LatestVersion = "ERROR";
                OnPropertyChanged(nameof(LatestVersion));
                OnPropertyChanged(nameof(LatestReleaseAssetSignedHashURL));
                OnPropertyChanged(nameof(LatestReleaseAssetURL));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        
        [NotifyPropertyChangedInvocator]
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public enum HotkeyType
        {
            Normal,
            EasterEgg
        }
    }
    

    public enum InternetConnectionStatus
    {
        Loading,
        NoAdapter,
        Disconnected,
        Connected
    }

}
