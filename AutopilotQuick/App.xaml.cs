using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using NLog;
using NLog.Targets;

namespace AutopilotQuick
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            SetupLoggingConfig();
            var mainWindow = new MainWindow();
            this.MainWindow = mainWindow;
            mainWindow.Closing += (sender, args2) =>
            {
                Environment.Exit(0);
            };
            mainWindow.Show();
        }


        public void SetupLoggingConfig()
        {
            var appFolder = Path.GetDirectoryName(Environment.ProcessPath);
            var LoggingConfig = new NLog.Config.LoggingConfiguration();
            FileVersionInfo v = FileVersionInfo.GetVersionInfo(GetExecutablePath());
            var logfile = new NLog.Targets.FileTarget("logfile")
            {
                FileName = $"{appFolder}/logs/latest.log",
                ArchiveFileName = $"{appFolder}/logs/{{#}}.log",
                ArchiveNumbering = ArchiveNumberingMode.Date,
                Layout = "${time:universalTime=True}|${level:uppercase=true}|${logger}|${message}",
                MaxArchiveFiles = 100,
                ArchiveOldFileOnStartup = true,
                ArchiveDateFormat = "yyyy-MM-dd HH_mm_ss",
                Header = $"AutopilotQuick version: {v.FileMajorPart}.{v.FileMinorPart}.{v.FileBuildPart}.{v.FilePrivatePart}\n",
                Footer = $"\nAutopilotQuick version: {v.FileMajorPart}.{v.FileMinorPart}.{v.FileBuildPart}.{v.FilePrivatePart}"
            };
            LoggingConfig.AddRule(LogLevel.Debug, LogLevel.Fatal, logfile);
            NLog.LogManager.Configuration = LoggingConfig;
        }
        public static string GetExecutablePath()
        {
            return Process.GetCurrentProcess().MainModule.FileName;
        }
    }
}
