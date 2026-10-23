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
                return SecretArchive.ReadBlob(file);
            }, token);
            token.ThrowIfCancellationRequested();
            return new LoadedSecretArchive(cached, false);
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
                return new LoadedSecretArchive(buffer.ToArray(), true);
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException || (ex is OperationCanceledException && !token.IsCancellationRequested))
            {
                progress.Report("Waiting for internet or the archive server. Retrying in 5 seconds...");
                await _retryDelay(token);
            }
        }
    }
}

public sealed record LoadedSecretArchive(byte[] Blob, bool Downloaded);
public sealed class ArchiveSourceException : Exception
{
    public ArchiveSourceException(string message) : base(message) { }
}
