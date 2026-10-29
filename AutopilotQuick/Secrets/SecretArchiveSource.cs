using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;
using AQ.SecretArchive;

namespace AutopilotQuick;

/// <summary>Startup archive loading without dependencies on the main window, telemetry, or InternetMan.</summary>
public sealed class SecretArchiveSource
{
    private readonly string _cachePath;
    private readonly Uri _address;
    private readonly HttpClient _client;
    private readonly Func<bool> _networkAvailable;
    private readonly Func<CancellationToken, Task> _retryDelay;

    public SecretArchiveSource(string cachePath, Uri address, HttpClient client,
        Func<bool>? networkAvailable = null, Func<CancellationToken, Task>? retryDelay = null)
    {
        if (address.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(address.UserInfo))
            throw new ArchiveSourceException("The archive URL must use HTTPS without embedded credentials.");
        _cachePath = cachePath;
        _address = address;
        _client = client;
        _networkAvailable = networkAvailable ?? NetworkInterface.GetIsNetworkAvailable;
        _retryDelay = retryDelay ?? (token => Task.Delay(TimeSpan.FromSeconds(5), token));
    }

    public async Task<LoadedSecretArchive> LoadAsync(bool downloadLatest, IProgress<string> progress, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!downloadLatest && File.Exists(_cachePath))
        {
            progress.Report("Reading the encrypted bundle cached on this drive...");
            var cached = await Task.Run(() =>
            {
                using var file = File.OpenRead(_cachePath);
                var blob = SecretArchive.ReadBlob(file);
                byte[]? checksum = null;
                if (File.Exists(_cachePath + ArchiveIntegrity.Suffix))
                {
                    using var sidecar = File.OpenRead(_cachePath + ArchiveIntegrity.Suffix);
                    checksum = ArchiveIntegrity.ReadChecksum(sidecar);
                }
                ArchiveIntegrity.Verify(blob, checksum);
                return new LoadedSecretArchive(blob, false, checksum);
            }, token);
            token.ThrowIfCancellationRequested();
            return cached;
        }

        while (true)
        {
            token.ThrowIfCancellationRequested();
            if (!_networkAvailable())
            {
                progress.Report("Waiting for internet to download the encrypted bundle...");
                await _retryDelay(token);
                continue;
            }
            try
            {
                progress.Report("Connecting to the archive server...");
                using var headerTimeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                headerTimeout.CancelAfter(TimeSpan.FromSeconds(10));
                using var response = await _client.GetAsync(_address, HttpCompletionOption.ResponseHeadersRead, headerTimeout.Token);
                if (response.StatusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500)
                {
                    progress.Report("Archive server is temporarily unavailable. Retrying in 5 seconds...");
                    await _retryDelay(token);
                    continue;
                }
                if (!response.IsSuccessStatusCode)
                    throw new ArchiveSourceException($"Archive server returned HTTP {(int)response.StatusCode}. Check the bundle URL and server access, then retry.");
                if (response.Content.Headers.ContentLength > SecretArchive.MaximumBlobBytes)
                    throw new ArchiveSourceException("The encrypted bundle exceeds the supported size limit.");
                progress.Report("Downloading the encrypted bundle...");
                await using var stream = await response.Content.ReadAsStreamAsync(token);
                using var buffer = new MemoryStream();
                var chunk = new byte[81920];
                while (true)
                {
                    // Limit stalled reads, not the entire transfer: the BIOS bundle is over 100 MiB.
                    using var idleTimeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                    idleTimeout.CancelAfter(TimeSpan.FromSeconds(30));
                    var count = await stream.ReadAsync(chunk.AsMemory(), idleTimeout.Token);
                    if (count == 0) break;
                    if (buffer.Length + count > SecretArchive.MaximumBlobBytes)
                        throw new ArchiveSourceException("The encrypted bundle exceeds the supported size limit.");
                    buffer.Write(chunk, 0, count);
                }
                token.ThrowIfCancellationRequested();
                if (response.Content.Headers.ContentLength is long expectedLength && buffer.Length != expectedLength)
                    throw new ArchiveIntegrityException("The encrypted bundle download is incomplete. Retry the download.");
                var blob = buffer.ToArray();
                ArchiveIntegrity.Verify(blob);
                progress.Report("Checking the encrypted bundle's integrity...");
                var checksum = await DownloadChecksumAsync(token);
                ArchiveIntegrity.Verify(blob, checksum);
                return new LoadedSecretArchive(blob, true, checksum);
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException || (ex is OperationCanceledException && !token.IsCancellationRequested))
            {
                progress.Report("Waiting for internet or the archive server. Retrying in 5 seconds...");
                await _retryDelay(token);
            }
        }
    }

    private async Task<byte[]?> DownloadChecksumAsync(CancellationToken token)
    {
        var address = new UriBuilder(_address) { Path = _address.AbsolutePath + ArchiveIntegrity.Suffix }.Uri;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        using var response = await _client.GetAsync(address, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        // Only a missing sidecar means legacy support. Other failures must not silently disable checking.
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        if (response.StatusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500)
            throw new HttpRequestException("Checksum server is temporarily unavailable.");
        if (!response.IsSuccessStatusCode)
            throw new ArchiveSourceException($"Checksum server returned HTTP {(int)response.StatusCode}. Check access to Secrets.aqsecrets.sha256, then retry.");
        if (response.Content.Headers.ContentLength > ArchiveIntegrity.MaximumChecksumBytes)
            throw new ArchiveIntegrityException("The bundle checksum file exceeds the supported size limit.");
        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
        using var buffer = new MemoryStream();
        var bytes = new byte[ArchiveIntegrity.MaximumChecksumBytes + 1];
        int count;
        while ((count = await stream.ReadAsync(bytes.AsMemory(0, bytes.Length - (int)buffer.Length), timeout.Token)) > 0)
        {
            buffer.Write(bytes, 0, count);
            if (buffer.Length > ArchiveIntegrity.MaximumChecksumBytes)
                throw new ArchiveIntegrityException("The bundle checksum file exceeds the supported size limit.");
        }
        if (response.Content.Headers.ContentLength is long expectedLength && buffer.Length != expectedLength)
            throw new ArchiveIntegrityException("The bundle checksum download is incomplete. Retry the download.");
        var checksum = buffer.ToArray();
        ArchiveIntegrity.ParseChecksum(checksum);
        return checksum;
    }

    /// <summary>Call only after successful decryption and app configuration validation. Never cache an unverified download.</summary>
    public async Task CacheValidatedAsync(LoadedSecretArchive archive, CancellationToken token)
    {
        ArchiveIntegrity.Verify(archive.Blob, archive.Checksum);
        if (!archive.Downloaded && archive.Checksum is not null) return;
        // Seed an offline checksum for legacy archives only after their AES-GCM authentication succeeded.
        var checksum = archive.Checksum ?? ArchiveIntegrity.CreateChecksum(archive.Blob);
        Directory.CreateDirectory(Path.GetDirectoryName(_cachePath)!);
        var temporary = _cachePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var temporaryChecksum = temporary + ArchiveIntegrity.Suffix;
        try
        {
            await File.WriteAllBytesAsync(temporaryChecksum, checksum, token);
            if (archive.Downloaded) await File.WriteAllBytesAsync(temporary, archive.Blob, token);
            token.ThrowIfCancellationRequested();
            // Each rename is atomic. An interruption between them fails closed with a mismatched checksum at next startup.
            if (archive.Downloaded) File.Move(temporary, _cachePath, true);
            File.Move(temporaryChecksum, _cachePath + ArchiveIntegrity.Suffix, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
            if (File.Exists(temporaryChecksum)) File.Delete(temporaryChecksum);
        }
    }
}

public sealed record LoadedSecretArchive(byte[] Blob, bool Downloaded, byte[]? Checksum = null);
public sealed class ArchiveSourceException : Exception
{
    public ArchiveSourceException(string message) : base(message) { }
}
