using System;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using AQ.SecretArchive;

namespace AutopilotQuick;

public partial class SecretUnlockWindow : Window
{
    private readonly CancellationTokenSource _cancel = new();
    private readonly string _cachePath = Path.Combine(App.GetExecutableFolder(), "Cache", "Secrets.aqsecrets");
    private CancellationTokenSource? _loading;
    private LoadedSecretArchive? _archive;
    public bool Unlocked { get; private set; }

    public SecretUnlockWindow()
    {
        InitializeComponent();
        Loaded += async (_, _) => await PrepareArchiveAsync();
        Closed += (_, _) => { _cancel.Cancel(); _loading?.Cancel(); _archive = null; };
    }

    private async void DownloadLatest_Changed(object sender, RoutedEventArgs e)
    {
        if (IsLoaded) await PrepareArchiveAsync();
    }

    private async void Retry_Click(object sender, RoutedEventArgs e) => await PrepareArchiveAsync();

    private async Task PrepareArchiveAsync()
    {
        _loading?.Cancel();
        using var loading = CancellationTokenSource.CreateLinkedTokenSource(_cancel.Token);
        _loading = loading;
        _archive = null;
        var loadingFinished = false;
        Unlock.IsEnabled = false;
        Passphrase.IsEnabled = false;
        Passphrase.Clear();
        try
        {
            var address = Environment.GetEnvironmentVariable("AQ_SECRET_ARCHIVE_URL") ??
                "https://nettools.psd202.org/AutoPilotFast/Secrets.aqsecrets";
            if (!Uri.TryCreate(address, UriKind.Absolute, out var uri))
                throw new ArchiveSourceException("The archive URL is invalid.");
            using var handler = new HttpClientHandler { AllowAutoRedirect = false };
            using var client = new HttpClient(handler) { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
            var source = new SecretArchiveSource(_cachePath, uri, client);
            var progress = new Progress<string>(message =>
            {
                if (!loadingFinished && !loading.IsCancellationRequested) Status.Text = message;
            });
            var archive = await source.LoadAsync(DownloadLatest.IsChecked == true, progress, loading.Token);
            loading.Token.ThrowIfCancellationRequested();
            _archive = archive;
            Status.Text = archive.Downloaded ? "Bundle downloaded. Enter the passphrase to unlock." : "Cached bundle ready. Enter the passphrase to unlock.";
            Passphrase.IsEnabled = true;
            Unlock.IsEnabled = true;
            Passphrase.Focus();
        }
        catch (OperationCanceledException) { }
        catch (ArchiveSourceException ex)
        {
            if (!loading.IsCancellationRequested) Status.Text = ex.Message;
        }
        catch (Exception)
        {
            if (!loading.IsCancellationRequested)
                Status.Text = "Could not read the encrypted bundle. Check the drive, or select Download latest bundle and retry.";
        }
        finally
        {
            loadingFinished = true;
            if (ReferenceEquals(_loading, loading)) _loading = null;
        }
    }

    private async void Unlock_Click(object sender, RoutedEventArgs e)
    {
        var archive = _archive;
        if (archive is null) return;
        Unlock.IsEnabled = false;
        Retry.IsEnabled = false;
        DownloadLatest.IsEnabled = false;
        Passphrase.IsEnabled = false;
        using var password = Passphrase.SecurePassword;
        Passphrase.Clear();
        try
        {
            Status.Text = "Unlocking...";
            var contents = await Task.Run(() => Decrypt(archive.Blob, password), _cancel.Token);
            try
            {
                _cancel.Token.ThrowIfCancellationRequested();
                AppSecrets.Validate(contents);
                if (archive.Downloaded) await CacheAsync(archive.Blob);
                _cancel.Token.ThrowIfCancellationRequested();
                AppSecrets.Initialize(contents);
                Unlocked = true;
                Close();
            }
            finally { if (!Unlocked) contents.Dispose(); }
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            // Never show parser/HTTP/crypto exception details or passphrase values.
            Status.Text = "Could not unlock. Check the passphrase and bundle configuration. Select Download latest bundle to replace an old or damaged cache.";
        }
        finally
        {
            if (!_cancel.IsCancellationRequested)
            {
                Unlock.IsEnabled = true;
                Retry.IsEnabled = true;
                DownloadLatest.IsEnabled = true;
                Passphrase.IsEnabled = true;
                Passphrase.Focus();
            }
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    private static ArchiveContents Decrypt(byte[] blob, SecureString password)
    {
        var pointer = Marshal.SecureStringToGlobalAllocUnicode(password);
        var chars = new char[password.Length];
        try
        {
            Marshal.Copy(pointer, chars, 0, chars.Length);
            return SecretArchive.Decrypt(blob, chars);
        }
        finally { Array.Clear(chars, 0, chars.Length); Marshal.ZeroFreeGlobalAllocUnicode(pointer); }
    }

    private async Task CacheAsync(byte[] blob)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_cachePath)!);
        var temporary = _cachePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(temporary, blob, _cancel.Token);
            _cancel.Token.ThrowIfCancellationRequested();
            File.Move(temporary, _cachePath, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
