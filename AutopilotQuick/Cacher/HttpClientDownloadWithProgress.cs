using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using Humanizer;
using MahApps.Metro.Controls;

namespace AutopilotQuick; 

public class HttpClientDownloadWithProgress : IDisposable
{
    private readonly string _downloadUrl;
    private readonly string _destinationFilePath;
    private readonly Bandwidth _bandwidth;

    private HttpClient _httpClient;
    

    public event EventHandler<DownloadProgressChangedEventArgs> ProgressChanged;
    

    public HttpClientDownloadWithProgress(string downloadUrl, string destinationFilePath)
    {
        _downloadUrl = downloadUrl;
        _destinationFilePath = destinationFilePath;
        _bandwidth = new Bandwidth();
    }

    public async Task StartDownload()
    {
        _httpClient = new HttpClient { Timeout = TimeSpan.FromDays(1) };
        using var response = await _httpClient.GetAsync(_downloadUrl, HttpCompletionOption.ResponseHeadersRead);
        await DownloadFileFromHttpResponseMessage(response);
    }

    private async Task DownloadFileFromHttpResponseMessage(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength;

        await using var contentStream = await response.Content.ReadAsStreamAsync();
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

        Application.Current.Invoke(() =>
        {
            Application.Current.Exit += (sender, args) =>
            {
                shouldStop = true;
            };
        });

        await using var fileStream = new FileStream(_destinationFilePath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize, true);
        do
        {
            var cts = new CancellationTokenSource();
            cts.CancelAfter(10.Seconds());
            var bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, cts.Token).ConfigureAwait(false);
            _bandwidth.CalculateSpeed(bytesRead);
            totalBytesRead += bytesRead;
            var a = new DownloadProgressChangedEventArgs("")
            {
                ProgressedByteSize = bytesRead,
                ReceivedBytesSize = totalBytesRead,
                TotalBytesToReceive = totalDownloadSize ?? totalBytesRead,
                AverageBytesPerSecondSpeed = _bandwidth.AverageSpeed,
                BytesPerSecondSpeed = _bandwidth.Speed
            };
            
            if (bytesRead == 0)
            {
                isMoreToRead = false;
                TriggerProgressChanged(a);
                continue;
            }
            
            await fileStream.WriteAsync(buffer, 0, bytesRead).ConfigureAwait(false);
            TriggerProgressChanged(a);
        }
        while (isMoreToRead && !shouldStop);
    }

    private void TriggerProgressChanged(DownloadProgressChangedEventArgs e)
    {
        if (ProgressChanged == null)
            return;
        

        ProgressChanged(this, e);
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
    }
}