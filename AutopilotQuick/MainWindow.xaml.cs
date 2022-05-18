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
using MahApps.Metro.Controls;

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
        public MainWindow()
        {
            InitializeComponent();
            context = new UserDataContext(DialogCoordinator.Instance);
            DataContext = context;
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
        private void MetroWindow_Loaded(object sender, RoutedEventArgs e)
        {
            Task.Factory.StartNew(Update, TaskCreationOptions.LongRunning);

            //TestUsers();
            if (Updated) this.ShowMessageAsync("Update successful!", "The update was successful.");
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

        public async void Update()
        {
            var version = new Version();
            var latestVersion = new Version();
            try
            {
                version = new Version(context.Version);
                latestVersion = new Version(context.LatestVersion);
            }
            catch (Exception e) { }
#if PUBLISH
            var PublicKey = Assembly.GetExecutingAssembly().GetManifestResourceStream("AutopilotQuick.Resources.AutopilotQuick_PubKey.asc");
            int maxStep = 6;
            if (!(latestVersion.CompareTo(version) > 0)) return;
            var selection = await context.DialogCoordinator.ShowMessageAsync(context, "An update is available",
                $"An update is available for {version.ToString(3)} to {latestVersion.ToString(3)}.",
                MessageDialogStyle.AffirmativeAndNegative, new MetroDialogSettings
                {
                    DefaultButtonFocus = MessageDialogResult.Affirmative
                });
            if (selection != MessageDialogResult.Affirmative) return;
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
                                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                                    "\\AutopilotQuick\\Update")))
                                {
                                    Directory.CreateDirectory(System.IO.Path.Join(
                                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                                        "\\AutopilotQuick\\Update"));
                                }

                                using (ZipArchive archive = ZipFile.OpenRead(downloadPath))
                                {
                                    try
                                    {
                                        var entry = archive.Entries.First(x => x.FullName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
                                        entry.ExtractToFile(Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
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

                                File.Move(Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "\\AutopilotQuick\\Update", "AutopilotQuick.exe"),
                                    Path.Combine(appFolder, appName + appExtension), true);
                                Application.Current.Invoke(() =>
                                {
                                    Process.Start(Path.Combine(appFolder, appName + appExtension));
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
        }

    }
}
