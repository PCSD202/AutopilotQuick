using MahApps.Metro.Controls.Dialogs;
using NLog;
using Octokit;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AutopilotQuick
{
    public class UserDataContext : INotifyPropertyChanged
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        public IDialogCoordinator DialogCoordinator { get; set; }

        public string Title { get; set; } = "Autopilot Quick";
        public string Version { get; set; }
        public string LatestVersion { get; set; }
        public string LatestReleaseAssetURL { get; set; }
        public string LatestReleaseAssetSignedHashURL { get; set; }

        private bool _connectedToInternet { get; set; }
        public bool ConnectedToInternet { get { return _connectedToInternet; } set { _connectedToInternet = value; OnPropertyChanged(nameof(ConnectedToInternet)); } }


        private double _totalProgress = 0;
        public double TotalProgress
        {
            get { return _totalProgress; }
            set { _totalProgress = value; OnPropertyChanged(nameof(TotalProgress)); }
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



        public UserDataContext(IDialogCoordinator dialogCoordinator)
        {
            DialogCoordinator = dialogCoordinator;
            FileVersionInfo v = FileVersionInfo.GetVersionInfo(App.GetExecutablePath());
            Version = $"{v.FileMajorPart}.{v.FileMinorPart}.{v.FileBuildPart}";
            OnPropertyChanged(nameof(Version));
            Title = $"Autopilot Quick - {Version}";
            OnPropertyChanged(nameof(Title));
        }

        public void RefreshLatestVersion()
        {
            while (!MainWindow.CheckForInternetConnection())
            {
                Thread.Sleep(200);
            }
            try
            {
                GitHubClient client = new GitHubClient(new ProductHeaderValue("AutopilotQuick", Version));
                Release latest = new Release();
                latest = client.Repository.Release.GetLatest("PCSD202", "AutopilotQuick").Result;

                LatestReleaseAssetURL = latest.Assets.First(x => x.Name == "AutopilotQuick.zip").BrowserDownloadUrl;
                if (latest.Assets.Any(x => x.Name == "AutopilotQuick.zip.sha256.pgp"))
                    LatestReleaseAssetSignedHashURL = latest.Assets.First(x => x.Name == "AutopilotQuick.zip.sha256.pgp").BrowserDownloadUrl;

                LatestVersion = $"{latest.TagName}";
                OnPropertyChanged(nameof(LatestVersion));
                OnPropertyChanged(nameof(LatestReleaseAssetSignedHashURL));
                OnPropertyChanged(nameof(LatestReleaseAssetURL));
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
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
