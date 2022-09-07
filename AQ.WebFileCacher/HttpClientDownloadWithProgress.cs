using System.IO.Abstractions;

namespace AQ.WebFileCacher;

public class HttpClientDownloadWithProgress : IDisposable
{
    private readonly string _downloadUrl;
    private readonly string _destinationFilePath;
    private readonly IFileSystem _fileSystem;

    private HttpClient _httpClient;

    public delegate void ProgressChangedHandler(long? totalFileSize, long totalBytesDownloaded, double? progressPercentage);

    public event ProgressChangedHandler ProgressChanged;

    public HttpClientDownloadWithProgress(string downloadUrl, string destinationFilePath, IFileSystem? fileSystem)
    {
        _downloadUrl = downloadUrl;
        _destinationFilePath = destinationFilePath;
        _fileSystem = fileSystem??new FileSystem();
    }

    public async Task StartDownload(CancellationToken ct)
    {
        _httpClient = new HttpClient { Timeout = TimeSpan.FromDays(1) };
        using (var response = await _httpClient.GetAsync(_downloadUrl, HttpCompletionOption.ResponseHeadersRead))
            await DownloadFileFromHttpResponseMessage(response);
    }

    private async Task DownloadFileFromHttpResponseMessage(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength;

        using (var contentStream = await response.Content.ReadAsStreamAsync())
            await ProcessContentStream(totalBytes, contentStream);
    }

    private async Task ProcessContentStream(long? totalDownloadSize, Stream contentStream)
    {
        const int bufferSize = 1024 * 256;
        var totalBytesRead = 0L;
        var readCount = 0L;
        var buffer = new byte[bufferSize];
        var isMoreToRead = true;
        var shouldStop = false;
        double? progressPercentage = null;
        if (totalDownloadSize.HasValue)
        {
            progressPercentage = Math.Round((double)totalBytesRead / totalDownloadSize.Value * 100, 2);
        }

        await using var fileStream = _fileSystem.FileStream.Create(_destinationFilePath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize, true);
        do
        {
            var bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length);
            if (bytesRead == 0)
            {
                isMoreToRead = false;
                TriggerProgressChanged(totalDownloadSize, totalBytesRead);
                continue;
            }

            await fileStream.WriteAsync(buffer, 0, bytesRead);

            totalBytesRead += bytesRead;
            readCount += 1;

            if (totalDownloadSize.HasValue)
            {
                if (progressPercentage.HasValue)
                {
                    if (Math.Abs(progressPercentage.Value - Math.Round((double)totalBytesRead / totalDownloadSize.Value * 100, 2)) > 0.1)
                    {
                        progressPercentage = Math.Round((double)totalBytesRead / totalDownloadSize.Value * 100, 2);
                        TriggerProgressChanged(totalDownloadSize, totalBytesRead);
                    };
                }
                
            }
            if (readCount % 100 == 0)
                TriggerProgressChanged(totalDownloadSize, totalBytesRead);
        }
        while (isMoreToRead && !shouldStop);
    }

    private void TriggerProgressChanged(long? totalDownloadSize, long totalBytesRead)
    {
        if (ProgressChanged == null)
            return;

        double? progressPercentage = null;
        if (totalDownloadSize.HasValue)
            progressPercentage = Math.Round((double)totalBytesRead / totalDownloadSize.Value * 100, 2);

        ProgressChanged(totalDownloadSize, totalBytesRead, progressPercentage);
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
    }
}