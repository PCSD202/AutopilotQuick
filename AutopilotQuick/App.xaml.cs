using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using AutopilotQuick.WMI;
using NLog;
using NLog.Config;
using NLog.LayoutRenderers;
using NLog.Targets;
using ORMi;
using Application = System.Windows.Application;

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
            AllocConsole();
            Console.WriteLine("Starting up");
            SetupLoggingConfig();
            
            for (int i = 0; i != e.Args.Length; ++i)
            {
                if (e.Args[i] == "/run")
                {
                    TaskManager.getInstance().Enabled = true;
                }
                else if (e.Args[i] == "/remove")
                {
                    TaskManager.getInstance().RemoveOnly = true;
                }
            }
            var mainWindow = new MainWindow();
            this.MainWindow = mainWindow;
            mainWindow.Closed += (sender, args2) =>
            {
                Environment.Exit(0);
            };

            Application.Current.Exit += (sender, args) =>
            {
                MainWindow.Close();
            };
            App.Current.ShutdownMode = ShutdownMode.OnMainWindowClose;
            mainWindow.Show();
        }
        


        public void SetupLoggingConfig()
        {
            ConfigurationItemFactory.Default.LayoutRenderers.RegisterDefinition("elapsedtime", typeof (ElapsedTimeLayoutRenderer));
            var appFolder = Path.GetDirectoryName(Environment.ProcessPath);
            var LoggingConfig = new NLog.Config.LoggingConfiguration();
            FileVersionInfo v = FileVersionInfo.GetVersionInfo(GetExecutablePath());
            WMIHelper helper = new WMIHelper("root\\CimV2");
            var model = helper.QueryFirstOrDefault<ComputerSystem>().Model;
            var serviceTag = helper.QueryFirstOrDefault<Bios>().SerialNumber;
            var logfile = new NLog.Targets.FileTarget("logfile")
            {
                FileName = $"{appFolder}/logs/latest.log",
                ArchiveFileName = $"{appFolder}/logs/{{#}}.log",
                ArchiveNumbering = ArchiveNumberingMode.Date,
                Layout = "${time:universalTime=True}|${elapsedtime}"+$"|{serviceTag}|{model}|"+"${level:uppercase=true}|${logger}|${message}",
                MaxArchiveFiles = 100,
                ArchiveOldFileOnStartup = true,
                ArchiveDateFormat = "yyyy-MM-dd HH_mm_ss",
                Header = $"AutopilotQuick version: {v.FileMajorPart}.{v.FileMinorPart}.{v.FileBuildPart}.{v.FilePrivatePart} DeviceID: {DeviceID.DeviceIdentifierMan.getInstance().GetDeviceIdentifier()}\n",
                Footer = $"\nAutopilotQuick version: {v.FileMajorPart}.{v.FileMinorPart}.{v.FileBuildPart}.{v.FilePrivatePart} DeviceID: {DeviceID.DeviceIdentifierMan.getInstance().GetDeviceIdentifier()}"
            };
            var logConsole = new NLog.Targets.ConsoleTarget("logconsole")
            {
                AutoFlush = true,
                DetectConsoleAvailable = false,
                Layout = "${time:universalTime=True}|${elapsedtime}" + $"|{serviceTag}|{model}|" +
                         "${level:uppercase=true}|${logger}|${message}",
                Header =
                    $"AutopilotQuick version: {v.FileMajorPart}.{v.FileMinorPart}.{v.FileBuildPart}.{v.FilePrivatePart} DeviceID: {DeviceID.DeviceIdentifierMan.getInstance().GetDeviceIdentifier()}\n",
                Footer =
                    $"\nAutopilotQuick version: {v.FileMajorPart}.{v.FileMinorPart}.{v.FileBuildPart}.{v.FilePrivatePart} DeviceID: {DeviceID.DeviceIdentifierMan.getInstance().GetDeviceIdentifier()}"
            };
            LoggingConfig.AddRule(LogLevel.Debug, LogLevel.Fatal, logfile);
            LoggingConfig.AddRule(LogLevel.Debug, LogLevel.Fatal, logConsole);
            NLog.LogManager.Configuration = LoggingConfig;
        }
        public static string GetExecutablePath()
        {
            return Process.GetCurrentProcess().MainModule.FileName;
        }
        
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AllocConsole();
        
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool FreeConsole();
    }
}
