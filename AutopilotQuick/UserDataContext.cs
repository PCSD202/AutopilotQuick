﻿using MahApps.Metro.Controls.Dialogs;
using NLog;
using Octokit;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
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



        public UserDataContext(IDialogCoordinator dialogCoordinator)
        {
            DialogCoordinator = dialogCoordinator;
            FileVersionInfo v = FileVersionInfo.GetVersionInfo(App.GetExecutablePath());
            Version = $"{v.FileMajorPart}.{v.FileMinorPart}.{v.FileBuildPart}";
            try
            {
                GitHubClient client = new GitHubClient(new ProductHeaderValue("AutopilotQuick", Version));
                Release latest = new Release();
                latest = client.Repository.Release.GetLatest("PCSD202", "AutopilotQuick").Result;

                LatestReleaseAssetURL = latest.Assets.First(x => x.Name == "AutopilotQuick.zip").BrowserDownloadUrl;
                if (latest.Assets.Any(x => x.Name == "AutopilotQuick.zip.sha256.pgp"))
                    LatestReleaseAssetSignedHashURL = latest.Assets.First(x => x.Name == "AutopilotQuick.zip.sha256.pgp").BrowserDownloadUrl;

                LatestVersion = $"{latest.TagName}";
                Title = $"Autopilot Quick - {Version}";
                OnPropertyChanged(nameof(Title));
            }
            catch (Exception e)
            {
                Logger.Error(e.ToString());
                LatestVersion = "ERROR";
            }

            OnPropertyChanged(nameof(LatestVersion));
            OnPropertyChanged(nameof(Version));
        }







        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
