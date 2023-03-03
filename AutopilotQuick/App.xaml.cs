#region

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using AQ.DeviceInfo;
using AQ.Watchdog;
using AutopilotQuick.DeviceID;
using AutopilotQuick.WMI;
using CommandLine;
using Humanizer;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.ApplicationInsights.WindowsServer.TelemetryChannel;
using Microsoft.ApplicationInsights.WorkerService;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.ApplicationInsights;
using Microsoft.Win32;
using Newtonsoft.Json;
using NLog;
using NLog.Config;
using NLog.Extensions.Logging;
using NLog.Targets;
using ORMi;
using Spectre.Console;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;

#endregion

namespace AutopilotQuick
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {

        public static bool Enabled = false;
        public static IServiceProvider ServiceProvider = null!;

        public static TelemetryClient telemetryClient;

        public static string SessionID = $"{Guid.NewGuid()}";

        private static string DataDir = GetRealExecutablePath();
        protected override async void OnStartup(StartupEventArgs e)
        {
            AllocConsole();
            
            var args = e.Args.Select(x=>x.Replace("/", "--")).ToArray();
            
            Parser.Default.ParseArguments<CommandLineOptions>(args)
                .WithParsed(o =>
                {
                    if (o.Enabled)
                    {
                        Enabled = true;
                    }

                    if (o.DataLocation != "")
                    {
                        DataDir = Base64Decode(o.DataLocation);
                    }
                });

            if (RunningOnWinPE() && Path.GetPathRoot(GetRealExecutablePath()).ToLower() != @"x:\")
            {
                //We must be on the flash drive
                //We need to copy ourselves to the ramdisk
                var copiedPath = CopySelfToRamdisk();
                var realPathB64 = Base64Encode(GetRealExecutablePath());
                var start = new ProcessStartInfo(copiedPath)
                {
                    Arguments = $"--run --data {realPathB64}"
                };
                Process.Start(start);
                Environment.Exit(0);
            }

            var s = Stopwatch.StartNew();
            Console.WriteLine("Starting up...");
            base.OnStartup(e);
            var loggingTasks = new List<Task>()
            {
                Task.Run(SetupLoggingConfig),
                Task.Run(() => { ServiceProvider = SetupAzureAppInsights(); })
            };
            await Task.WhenAll(loggingTasks);
            Console.WriteLine($"Startup took {s.Elapsed.Humanize()}.");
            s.Restart();
            var _logger = GetLogger<App>();
            _logger.LogInformation($"Started logging service in {s.Elapsed.Humanize()}");
            var telemetryClient = GetTelemetryClient();
            LogManager.AutoShutdown = true;


#if PUBLISH
            Watchdog.Instance.ConfigureLogger(GetLogger<Watchdog>()); //Configure logging
            
            Watchdog.Instance.HandleArgs(args);
#endif
            
            
            
            var mainWindow = new MainWindow();
            
            this.MainWindow = mainWindow;
            mainWindow.Closed += (sender, args2) =>
            {
                FlushTelemetry();
                Environment.Exit(0);
            };

            Current.Exit += (sender, args) =>
            {
                FlushTelemetry();
                MainWindow.Close();
                LogManager.Shutdown();
            };
            Current.DispatcherUnhandledException += CurrentOnDispatcherUnhandledException;
            Current.ShutdownMode = ShutdownMode.OnMainWindowClose;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomainOnUnhandledException;
            mainWindow.Show();
            
        }

        
        private static string CopySelfToRamdisk()
        {
            var ramDiskFolderPath = @"X:\AQ";
            
            //Create folder on ramdisk to hold autopilotQuick
            if (Directory.Exists(ramDiskFolderPath))
            {
                Directory.Delete(ramDiskFolderPath, true);
            }

            Directory.CreateDirectory(ramDiskFolderPath);
            var aqRamdiskPath = Path.Join(ramDiskFolderPath, Path.GetFileName(GetRealExecutablePath()));
            var fileCopier = new CustomFileCopier(GetRealExecutablePath(), aqRamdiskPath);
            AnsiConsole.Progress()
                .AutoClear(false) // Do not remove the task list when done
                .Columns(new ProgressColumn[]
                {
                    new TaskDescriptionColumn(), // Task description
                    new ProgressBarColumn(), // Progress bar
                    new PercentageColumn(), // Percentage
                    new SpinnerColumn(), // Spinner
                })
                .Start(ctx =>
                {
                    var copyTask = ctx.AddTask("[green]Copying to ramdisk[/]");
                    fileCopier.OnProgressChanged += (long size, long downloaded, double percentage, ref bool cancel) =>
                    {
                        copyTask.MaxValue = size;
                        copyTask.Value = downloaded;
                    };
                    fileCopier.OnComplete += () =>
                    {
                        copyTask.StopTask();
                    };
                    fileCopier.Copy();
                });
            return aqRamdiskPath;
        }

        public static string Base64Encode(string plainText) {
            var plainTextBytes = Encoding.UTF8.GetBytes(plainText);
            return Convert.ToBase64String(plainTextBytes);
        }
        
        public static string Base64Decode(string base64EncodedData) {
            var base64EncodedBytes = Convert.FromBase64String(base64EncodedData);
            return Encoding.UTF8.GetString(base64EncodedBytes);
        }

        private void CurrentDomainOnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            GetLogger<App>().LogError((Exception?)e.ExceptionObject, "Unhandled exception: {e}", (Exception?)e.ExceptionObject);
            if (e.IsTerminating)
            {
                GetLogger<App>().LogError("App terminating");
                FlushTelemetry();
                LogManager.Flush();
                LogManager.Shutdown();
            }
        }

        public static bool RunningOnWinPE()
        {
            var key = Registry.LocalMachine.OpenSubKey("System\\ControlSet001\\Control\\MiniNT");
            return key != null;
        }

        private void CurrentOnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            GetLogger<App>().LogError(e.Exception, "Unhandled exception: {e}", e.Exception);
        }

        public static FileVersionInfo GetVersion()
        {
            return FileVersionInfo.GetVersionInfo(GetExecutablePath());
        }

        public static DiskDrive GetBootDrive()
        {
            var EnvironmentDrive = Registry.GetValue("HKEY_LOCAL_MACHINE\\System\\CurrentControlSet\\Control", "PEBootRamdiskSourceDrive", null);
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
            
            public string? version = null;
            public DiskDrive? bootDrive = null;
            public void Initialize(ITelemetry telemetry)
            {

                if (version is null)
                {
                    FileVersionInfo v = FileVersionInfo.GetVersionInfo(GetExecutablePath());
                    version = $"{v.FileMajorPart}.{v.FileMinorPart}.{v.FileBuildPart}";
                }

                if (bootDrive is null)
                {
                    bootDrive = GetBootDrive();
                }
                
                telemetry.Context.User.Id = DeviceIdentifierMan.getInstance().GetDeviceIdentifier();
                telemetry.Context.GlobalProperties["DeviceID"] = DeviceIdentifierMan.getInstance().GetDeviceIdentifier();
                telemetry.Context.GlobalProperties["ServiceTag"] = DeviceInfo.ServiceTag;
                telemetry.Context.GlobalProperties["Model"] = DeviceInfo.DeviceModel;
                telemetry.Context.GlobalProperties["DriveModel"] = bootDrive.Model;
                telemetry.Context.GlobalProperties["Drive"] = JsonConvert.SerializeObject(bootDrive);
                telemetry.Context.GlobalProperties["BatteryHealthPercent"] = $"{DeviceInfo.BatteryHealth}";
                telemetry.Context.Component.Version = version;
                telemetry.Context.Session.Id = SessionID;
            }
        }

        public IServiceProvider SetupAzureAppInsights()
        {
            // Create the DI container.
            IServiceCollection services = new ServiceCollection();
            

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
            services.AddSingleton<ITelemetryInitializer, MyTelemetryInitializer>();

            var appFolder = GetExecutableFolder();
            var telemetryChannel = new ServerTelemetryChannel();
            telemetryChannel.StorageFolder = Path.Combine(appFolder, "logs", "ApplicationInsights");
            Directory.CreateDirectory(telemetryChannel.StorageFolder);
            services.AddSingleton(typeof(ITelemetryChannel), telemetryChannel);
            
            services.AddApplicationInsightsTelemetryWorkerService(options);
            
            // Being a regular console app, there is no appsettings.json or configuration providers enabled by default.
            // Hence instrumentation key/ connection string and any changes to default logging level must be specified here.
            services.AddLogging(loggingBuilder =>
            {
                loggingBuilder
                    .AddFilter<ApplicationInsightsLoggerProvider>(
                        "", LogLevel.Information);
                loggingBuilder.AddNLog();
            });
            
            // Build ServiceProvider.
            IServiceProvider serviceProvider = services.BuildServiceProvider();

            // Obtain TelemetryClient instance from DI, for additional manual tracking or to flush.
            var telemetryClient = serviceProvider.GetRequiredService<TelemetryClient>();

            App.telemetryClient = telemetryClient;
            return serviceProvider;
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
            ConfigurationItemFactory.Default.LayoutRenderers.RegisterDefinition("elapsedtime", typeof (ElapsedTimeLayoutRenderer));
            var appFolder = GetExecutableFolder();
            var LoggingConfig = new LoggingConfiguration();
            FileVersionInfo v = FileVersionInfo.GetVersionInfo(GetExecutablePath());
            var model = DeviceInfo.DeviceModel;
            var serviceTag = DeviceInfo.ServiceTag;
            var logfile = new FileTarget("logfile")
            {
                FileName = $"{appFolder}/logs/latest.log",
                ArchiveFileName = $"{appFolder}/logs/{{#}}.log",
                ArchiveNumbering = ArchiveNumberingMode.Date,
                Layout = "${time:universalTime=True}|${elapsedtime}"+$"|{serviceTag}|{model}|"+"${level:uppercase=true}|${logger}|${message}",
                MaxArchiveFiles = 100,
                ArchiveOldFileOnStartup = true,
                ArchiveDateFormat = "yyyy-MM-dd HH_mm_ss",
                Header = $"AutopilotQuick version: {v.FileMajorPart}.{v.FileMinorPart}.{v.FileBuildPart}.{v.FilePrivatePart} DeviceID: {DeviceIdentifierMan.getInstance().GetDeviceIdentifier()}\n",
                Footer = $"\nAutopilotQuick version: {v.FileMajorPart}.{v.FileMinorPart}.{v.FileBuildPart}.{v.FilePrivatePart} DeviceID: {DeviceIdentifierMan.getInstance().GetDeviceIdentifier()}"
            };
            var logConsole = new ConsoleTarget("logconsole")
            {
                AutoFlush = true,
                DetectConsoleAvailable = false,
                Layout = "${time:universalTime=True}|${elapsedtime}" + $"|{serviceTag}|{model}|" +
                         "${level:uppercase=true}|${logger}|${message}",
                Header =
                    $"AutopilotQuick version: {v.FileMajorPart}.{v.FileMinorPart}.{v.FileBuildPart}.{v.FilePrivatePart} DeviceID: {DeviceIdentifierMan.getInstance().GetDeviceIdentifier()}\n",
                Footer =
                    $"\nAutopilotQuick version: {v.FileMajorPart}.{v.FileMinorPart}.{v.FileBuildPart}.{v.FilePrivatePart} DeviceID: {DeviceIdentifierMan.getInstance().GetDeviceIdentifier()}"
            };
            LoggingConfig.AddRule( NLog.LogLevel.Debug, NLog.LogLevel.Fatal, logfile);
            LoggingConfig.AddRule( NLog.LogLevel.Debug,  NLog.LogLevel.Fatal, logConsole);
            LogManager.Configuration = LoggingConfig;
        }
        
        
        //This is the real path to the executable, on the ramdisk
        public static string GetRealExecutablePath()
        {
            return Process.GetCurrentProcess().MainModule.FileName;
        }
        
        //This is the fake path to the executable on the flashDrive
        public static string GetExecutablePath()
        {
            return DataDir;
        }

        public static string GetExecutableFolder()
        {
            return Path.GetDirectoryName(GetExecutablePath());
        }
        
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AllocConsole();
        
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool FreeConsole();
    }
}
