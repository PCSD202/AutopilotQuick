using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using AQ.DeviceIdentifier;
using AutopilotQuick.WMI;
using MahApps.Metro.Controls;
using MahApps.Metro.Controls.Dialogs;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.ApplicationInsights.WindowsServer.TelemetryChannel;
using Microsoft.ApplicationInsights.WorkerService;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using NLog;
using NLog.Config;
using NLog.Extensions.Logging;
using NLog.LayoutRenderers;
using NLog.Targets;
using ORMi;
using Application = System.Windows.Application;
using ILogger = Microsoft.Extensions.Logging.ILogger;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace AutopilotQuick
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        public static IServiceProvider ServiceProvider = null!;

        public static TelemetryClient telemetryClient;

        public static string SessionID = $"{Guid.NewGuid()}";

        public App()
        {
            ServiceCollection services = new ServiceCollection();
            ConfigureServices(services);
            ServiceProvider = services.BuildServiceProvider();
            
            // Obtain TelemetryClient instance from DI, for additional manual tracking or to flush.
            App.telemetryClient = ServiceProvider.GetRequiredService<TelemetryClient>();
        }
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            AllocConsole();
            Console.WriteLine("Starting up");
            SetupLoggingConfig();
            var _logger = GetLogger<App>();
            var telemetryClient = GetTelemetryClient();

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
            
            var mainWindow = ServiceProvider.GetService<MainWindow>();
            MainWindow = mainWindow;
            mainWindow.Closed += (sender, args2) =>
            {
                FlushTelemetry();
                Environment.Exit(0);
            };

            Current.Exit += (sender, args) =>
            {
                FlushTelemetry();
                MainWindow.Close();
            };
            Current.ShutdownMode = ShutdownMode.OnMainWindowClose;
            mainWindow.Show();
        }
        
        private void ConfigureServices(ServiceCollection services)
        {
            var options = new ApplicationInsightsServiceOptions()
            {
                ConnectionString = "InstrumentationKey=0a61199e-61f5-4c00-a8ed-c802260b9665;IngestionEndpoint=https://northcentralus-0.in.applicationinsights.azure.com/;LiveEndpoint=https://northcentralus.livediagnostics.monitor.azure.com/",
                EnableDependencyTrackingTelemetryModule = false,
                EnablePerformanceCounterCollectionModule = false,
                EnableAdaptiveSampling = false,
                EnableQuickPulseMetricStream = true,
#if PUBLISH
                DeveloperMode = false
#endif
#if !PUBLISH
                DeveloperMode = true
#endif
            };
            

            var appFolder = Path.GetDirectoryName(Environment.ProcessPath);
            var telemetryChannel = new ServerTelemetryChannel();
            telemetryChannel.StorageFolder = Path.Combine(appFolder, "logs", "ApplicationInsights");
            Directory.CreateDirectory(telemetryChannel.StorageFolder);
            services.AddSingleton(typeof(ITelemetryChannel), telemetryChannel);
            services.AddSingleton<ITelemetryInitializer, MyTelemetryInitializer>();
            
            services.AddApplicationInsightsTelemetryWorkerService(options);
            
            // Being a regular console app, there is no appsettings.json or configuration providers enabled by default.
            // Hence instrumentation key/ connection string and any changes to default logging level must be specified here.
            services.AddLogging(loggingBuilder =>
            {
                loggingBuilder
                    .AddFilter<Microsoft.Extensions.Logging.ApplicationInsights.ApplicationInsightsLoggerProvider>(
                        "", LogLevel.Information);
                loggingBuilder.AddNLog();
            });
            services.AddSingleton<MainWindow>();

            services.AddOptions<DeviceIdentifierServiceOptions>().Configure(x =>
            {
                x.StoragePath = Path.Join(Path.GetDirectoryName(GetExecutablePath()), "DeviceIdentifier.json");
            });
            
            services.AddSingleton<IFileSystem, FileSystem>();
            //Register the DeviceIdentifierService
            services.AddSingleton<IDeviceIdentifierService, DeviceIdentifierService>();

        }
        

        public static FileVersionInfo GetVersion()
        {
            return FileVersionInfo.GetVersionInfo(GetExecutablePath());
        }

        public static DiskDrive GetBootDrive()
        {
            var EnvironmentDrive = Microsoft.Win32.Registry.GetValue("HKEY_LOCAL_MACHINE\\System\\CurrentControlSet\\Control", "PEBootRamdiskSourceDrive", null);
            WMIHelper helper = new WMIHelper("root\\CimV2");
            var drives = helper.Query<DiskDrive>();
            if (EnvironmentDrive is not null)
            {
                var eDrivestr = ((string)EnvironmentDrive).TrimEnd('\\');
                var bootDisk = helper.Query($"ASSOCIATORS OF {{Win32_LogicalDisk.DeviceID='{eDrivestr}'}} WHERE AssocClass=Win32_LogicalDiskToPartition").First();
                if (bootDisk is not null)
                {
                    string pattern = @"^Disk #([0-9]+), Partition #[0-9]+";
                    var diskName = (string)bootDisk.Name;
                    var matches = Regex.Match(diskName, pattern);
                    var diskFullName = @"\\.\PHYSICALDRIVE"+matches.Groups[1].Value;
                    DiskDrive diskToSelect = drives.First(x => x.DeviceID == diskFullName);
                    return diskToSelect;
                }
            }

            return drives.First();
        }
        
        public class MyTelemetryInitializer : ITelemetryInitializer
        {
            public static string SessionID = App.SessionID;
            public string? model = null;
            public string? serviceTag = null;
            public string? version = null;
            public DiskDrive? bootDrive = null;
            public IDeviceIdentifierService DeviceIdentifierService;

            public MyTelemetryInitializer(IDeviceIdentifierService deviceIdentifierService)
            {
                DeviceIdentifierService = deviceIdentifierService;
            }

            public void Initialize(ITelemetry telemetry)
            {
                if (model is null)
                {
                    WMIHelper helper = new WMIHelper("root\\CimV2");
                    model = helper.QueryFirstOrDefault<ComputerSystem>().Model;
                }

                if (serviceTag is null)
                {
                    WMIHelper helper = new WMIHelper("root\\CimV2");
                    serviceTag = helper.QueryFirstOrDefault<Bios>().SerialNumber;
                }

                if (version is null)
                {
                    FileVersionInfo v = FileVersionInfo.GetVersionInfo(App.GetExecutablePath());
                    version = $"{v.FileMajorPart}.{v.FileMinorPart}.{v.FileBuildPart}";
                }

                if (bootDrive is null)
                {
                    bootDrive = GetBootDrive();
                }

                telemetry.Context.User.Id = DeviceIdentifierService.Get();
                telemetry.Context.GlobalProperties["DeviceID"] = DeviceIdentifierService.Get();
                telemetry.Context.GlobalProperties["ServiceTag"] = serviceTag;
                telemetry.Context.GlobalProperties["Model"] = model;
                telemetry.Context.GlobalProperties["DriveModel"] = bootDrive.Model;
                telemetry.Context.GlobalProperties["Drive"] = JsonConvert.SerializeObject(bootDrive);
                telemetry.Context.Component.Version = version;
                telemetry.Context.Session.Id = SessionID;
            }
        }


        public static IDeviceIdentifierService GetDeviceIDService()
        {
            return ServiceProvider.GetRequiredService<IDeviceIdentifierService>();
        }

        public static ILogger<T> GetLogger<T>()
        {
            return ServiceProvider.GetRequiredService<ILogger<T>>();
        }

        public static TelemetryClient GetTelemetryClient()
        {
            return telemetryClient;
        }
        
        public static bool FlushTelemetry()
        {
            var telemetryClient = GetTelemetryClient();
            return telemetryClient.FlushAsync(CancellationToken.None).GetAwaiter().GetResult();
        }
        public static Task<bool> FlushTelemetryAsync(CancellationToken? ct = null)
        {
            ct ??= CancellationToken.None;
            var telemetryClient = GetTelemetryClient();
            return telemetryClient.FlushAsync(ct.Value);
        }

        public void SetupLoggingConfig()
        {
            IDeviceIdentifierService deviceIdentifierService = ServiceProvider.GetService<IDeviceIdentifierService>() ?? throw new InvalidOperationException();
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
                Header = $"AutopilotQuick version: {v.FileMajorPart}.{v.FileMinorPart}.{v.FileBuildPart}.{v.FilePrivatePart} DeviceID: {deviceIdentifierService.Get()}\n",
                Footer = $"\nAutopilotQuick version: {v.FileMajorPart}.{v.FileMinorPart}.{v.FileBuildPart}.{v.FilePrivatePart} DeviceID: {deviceIdentifierService.Get()}"
            };
            var logConsole = new NLog.Targets.ConsoleTarget("logconsole")
            {
                AutoFlush = true,
                DetectConsoleAvailable = false,
                Layout = "${time:universalTime=True}|${elapsedtime}" + $"|{serviceTag}|{model}|" +
                         "${level:uppercase=true}|${logger}|${message}",
                Header =
                    $"AutopilotQuick version: {v.FileMajorPart}.{v.FileMinorPart}.{v.FileBuildPart}.{v.FilePrivatePart} DeviceID: {deviceIdentifierService.Get()}\n",
                Footer =
                    $"\nAutopilotQuick version: {v.FileMajorPart}.{v.FileMinorPart}.{v.FileBuildPart}.{v.FilePrivatePart} DeviceID: {deviceIdentifierService.Get()}"
            };
            LoggingConfig.AddRule( NLog.LogLevel.Debug, NLog.LogLevel.Fatal, logfile);
            LoggingConfig.AddRule( NLog.LogLevel.Debug,  NLog.LogLevel.Fatal, logConsole);
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
