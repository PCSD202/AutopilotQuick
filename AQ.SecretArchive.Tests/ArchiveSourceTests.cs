using System.Net;
using AutopilotQuick;
using NUnit.Framework;

namespace AQ.SecretArchive.Tests;

[TestFixture]
public class ArchiveSourceTests
{
    private static readonly Uri Address = new("https://example.invalid/Secrets.aqsecrets");

    [Test]
    public async Task CachedBundleLoadsOfflineWithoutAnyNetworkRequest()
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllBytesAsync(path, new byte[] { 1, 2, 3 });
            using var client = new HttpClient(new Handler(_ => throw new AssertionException("Network should not be used.")));
            var source = new SecretArchiveSource(path, Address, client, () => false);
            var loaded = await source.LoadAsync(false, new ProgressLog(), CancellationToken.None);
            Assert.That(loaded.Downloaded, Is.False);
            Assert.That(loaded.Blob, Is.EqualTo(new byte[] { 1, 2, 3 }));
        }
        finally { File.Delete(path); }
    }

    [Test]
    public async Task MissingCacheWaitsForNetworkThenDownloads()
    {
        var connected = false;
        var waits = 0;
        var requests = 0;
        var progress = new ProgressLog();
        using var client = new HttpClient(new Handler(_ =>
        {
            requests++;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[] { 42 }) };
        }));
        var source = new SecretArchiveSource(MissingPath(), Address, client, () => connected, token =>
        {
            token.ThrowIfCancellationRequested();
            waits++;
            connected = true;
            return Task.CompletedTask;
        });
        var loaded = await source.LoadAsync(false, progress, CancellationToken.None);
        Assert.That(waits, Is.EqualTo(1));
        Assert.That(requests, Is.EqualTo(1));
        Assert.That(loaded.Downloaded, Is.True);
        Assert.That(progress.Messages, Has.Some.Contains("Waiting for internet"));
    }

    [Test]
    public async Task DownloadLatestBypassesCacheAndRetriesConnectionFailure()
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllBytesAsync(path, new byte[] { 0 });
            var requests = 0;
            var waits = 0;
            using var client = new HttpClient(new Handler(_ =>
            {
                if (++requests == 1) throw new HttpRequestException("synthetic connection failure");
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[] { 42 }) };
            }));
            var source = new SecretArchiveSource(path, Address, client, () => true, _ => { waits++; return Task.CompletedTask; });
            var loaded = await source.LoadAsync(true, new ProgressLog(), CancellationToken.None);
            Assert.That(requests, Is.EqualTo(2));
            Assert.That(waits, Is.EqualTo(1));
            Assert.That(loaded.Blob, Is.EqualTo(new byte[] { 42 }));
            Assert.That(await File.ReadAllBytesAsync(path), Is.EqualTo(new byte[] { 0 }), "Download must not replace cache before validation.");
        }
        finally { File.Delete(path); }
    }

    [Test]
    public void WaitingCanBeCanceled()
    {
        using var cancel = new CancellationTokenSource();
        using var client = new HttpClient(new Handler(_ => throw new AssertionException("Network should not be used.")));
        var source = new SecretArchiveSource(MissingPath(), Address, client, () => false, token =>
        {
            cancel.Cancel();
            token.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        });
        Assert.ThrowsAsync<OperationCanceledException>(async () => await source.LoadAsync(false, new ProgressLog(), cancel.Token));
    }

    [TestCase(HttpStatusCode.NotFound)]
    [TestCase(HttpStatusCode.Forbidden)]
    [TestCase(HttpStatusCode.Redirect)]
    public void PermanentHttpFailuresAreActionableWithoutInfiniteRetry(HttpStatusCode status)
    {
        using var client = new HttpClient(new Handler(_ => new HttpResponseMessage(status)));
        var source = new SecretArchiveSource(MissingPath(), Address, client, () => true,
            _ => throw new AssertionException("Permanent failures should not retry."));
        var error = Assert.ThrowsAsync<ArchiveSourceException>(async () => await source.LoadAsync(false, new ProgressLog(), CancellationToken.None));
        Assert.That(error!.Message, Does.Contain(((int)status).ToString()));
    }

    private static string MissingPath() => Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "Secrets.aqsecrets");
    private sealed class ProgressLog : IProgress<string>
    {
        public List<string> Messages { get; } = new();
        public void Report(string value) => Messages.Add(value);
    }
    private sealed class Handler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _send;
        public Handler(Func<HttpRequestMessage, HttpResponseMessage> send) => _send = send;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_send(request));
        }
    }
}
