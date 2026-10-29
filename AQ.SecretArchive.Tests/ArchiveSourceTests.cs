using System.Net;
using System.Text;
using AutopilotQuick;
using NUnit.Framework;

namespace AQ.SecretArchive.Tests;

[TestFixture]
public class ArchiveSourceTests
{
    private static readonly Uri Address = new("https://example.invalid/Secrets.aqsecrets");
    private byte[] _blob = null!;

    [OneTimeSetUp]
    public void CreateSyntheticArchive()
    {
        using var contents = new ArchiveContents();
        contents.Add("test.txt", Encoding.UTF8.GetBytes("synthetic-only"));
        _blob = SecretArchive.Encrypt(contents, "test-only-password");
    }

    [Test]
    public async Task CachedBundleLoadsOfflineWithoutAnyNetworkRequest()
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllBytesAsync(path, _blob);
            using var client = new HttpClient(new Handler(_ => throw new AssertionException("Network should not be used.")));
            var source = new SecretArchiveSource(path, Address, client, () => false);
            var loaded = await source.LoadAsync(false, new ProgressLog(), CancellationToken.None);
            Assert.That(loaded.Downloaded, Is.False);
            Assert.That(loaded.Blob, Is.EqualTo(_blob));
            Assert.That(loaded.Checksum, Is.Null, "A legacy cache must not claim checksum verification.");
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
        using var client = new HttpClient(new Handler(request =>
        {
            requests++;
            return Reply(request);
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
        Assert.That(requests, Is.EqualTo(2));
        Assert.That(loaded.Downloaded, Is.True);
        Assert.That(loaded.Checksum, Is.Null, "HTTP 404 on the sidecar supports existing servers.");
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
            using var client = new HttpClient(new Handler(request =>
            {
                if (++requests == 1) throw new HttpRequestException("synthetic connection failure");
                return Reply(request);
            }));
            var source = new SecretArchiveSource(path, Address, client, () => true, _ => { waits++; return Task.CompletedTask; });
            var loaded = await source.LoadAsync(true, new ProgressLog(), CancellationToken.None);
            Assert.That(requests, Is.EqualTo(3));
            Assert.That(waits, Is.EqualTo(1));
            Assert.That(loaded.Blob, Is.EqualTo(_blob));
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

    [Test]
    public async Task ChecksumDownloadsAndCachesWithBlobThenDetectsOfflineCorruption()
    {
        var path = MissingPath();
        var checksum = ArchiveIntegrity.CreateChecksum(_blob);
        using var client = new HttpClient(new Handler(request => Reply(request, checksum)));
        var source = new SecretArchiveSource(path, Address, client, () => true);
        try
        {
            var loaded = await source.LoadAsync(true, new ProgressLog(), CancellationToken.None);
            Assert.That(loaded.Checksum, Is.EqualTo(checksum));
            Assert.That(File.Exists(path), Is.False, "Loading must not replace cache before decryption.");
            using var contents = SecretArchive.Decrypt(loaded.Blob, "test-only-password");
            await source.CacheValidatedAsync(loaded, CancellationToken.None);

            using var offline = new HttpClient(new Handler(_ => throw new AssertionException("Offline cache must not request the network.")));
            var cachedSource = new SecretArchiveSource(path, Address, offline, () => false);
            Assert.That((await cachedSource.LoadAsync(false, new ProgressLog(), CancellationToken.None)).Checksum, Is.EqualTo(checksum));
            var damaged = _blob.ToArray();
            damaged[44] ^= 1; // Same length, damaged ciphertext.
            await File.WriteAllBytesAsync(path, damaged);
            var error = Assert.ThrowsAsync<ArchiveIntegrityException>(async () => await cachedSource.LoadAsync(false, new ProgressLog(), CancellationToken.None));
            Assert.That(error!.Message, Does.Contain("does not match"));
        }
        finally { if (Directory.Exists(Path.GetDirectoryName(path))) Directory.Delete(Path.GetDirectoryName(path)!, true); }
    }

    [Test]
    public async Task SuccessfulLegacyUnlockSeedsLocalChecksumWithoutChangingBlob()
    {
        var path = MissingPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, _blob);
        using var client = new HttpClient(new Handler(_ => throw new AssertionException("No network allowed.")));
        var source = new SecretArchiveSource(path, Address, client, () => false);
        try
        {
            var loaded = await source.LoadAsync(false, new ProgressLog(), CancellationToken.None);
            using var contents = SecretArchive.Decrypt(loaded.Blob, "test-only-password");
            await source.CacheValidatedAsync(loaded, CancellationToken.None);
            Assert.That(await File.ReadAllBytesAsync(path), Is.EqualTo(_blob));
            Assert.That((await source.LoadAsync(false, new ProgressLog(), CancellationToken.None)).Checksum, Is.Not.Null);
        }
        finally { Directory.Delete(Path.GetDirectoryName(path)!, true); }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void TruncatedOrLengthPreservingDamagedDownloadDoesNotReplaceExistingCache(bool sameLength)
    {
        var path = Path.GetTempFileName();
        var original = new byte[] { 7, 8, 9 };
        File.WriteAllBytes(path, original);
        var damaged = sameLength ? _blob.ToArray() : _blob[..^1];
        if (sameLength) damaged[44] ^= 1;
        using var client = new HttpClient(new Handler(request => Reply(request, ArchiveIntegrity.CreateChecksum(_blob), damaged)));
        var source = new SecretArchiveSource(path, Address, client, () => true,
            _ => throw new AssertionException("Integrity failures should be shown, not hidden by network retries."));
        try
        {
            Assert.ThrowsAsync<ArchiveIntegrityException>(async () => await source.LoadAsync(true, new ProgressLog(), CancellationToken.None));
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(original));
        }
        finally { File.Delete(path); }
    }

    [Test]
    public void HttpContentLengthMismatchIsRejectedEvenWhenArchiveStructureIsValid()
    {
        using var client = new HttpClient(new Handler(_ =>
        {
            var content = new ByteArrayContent(_blob);
            content.Headers.ContentLength = _blob.Length + 1;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        }));
        var source = new SecretArchiveSource(MissingPath(), Address, client, () => true);
        var error = Assert.ThrowsAsync<ArchiveIntegrityException>(async () => await source.LoadAsync(true, new ProgressLog(), CancellationToken.None));
        Assert.That(error!.Message, Does.Contain("incomplete"));
    }

    [TestCase(HttpStatusCode.Forbidden)]
    [TestCase(HttpStatusCode.Redirect)]
    public void SidecarHttpFailuresDoNotSilentlyDowngradeToLegacy(HttpStatusCode status)
    {
        using var client = new HttpClient(new Handler(request => IsChecksum(request) ? new HttpResponseMessage(status) : Reply(request)));
        var source = new SecretArchiveSource(MissingPath(), Address, client, () => true);
        var error = Assert.ThrowsAsync<ArchiveSourceException>(async () => await source.LoadAsync(true, new ProgressLog(), CancellationToken.None));
        Assert.That(error!.Message, Does.Contain("Checksum server").And.Contain(((int)status).ToString()));
    }

    [TestCase("<html>not a checksum</html>")]
    [TestCase("0123456789abcdef")]
    public void MalformedSidecarIsRejected(string checksum)
    {
        using var client = new HttpClient(new Handler(request => Reply(request, Encoding.ASCII.GetBytes(checksum))));
        var source = new SecretArchiveSource(MissingPath(), Address, client, () => true);
        Assert.ThrowsAsync<ArchiveIntegrityException>(async () => await source.LoadAsync(true, new ProgressLog(), CancellationToken.None));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void OversizedSidecarIsRejectedWithOrWithoutContentLength(bool unknownLength)
    {
        using var client = new HttpClient(new Handler(request =>
        {
            if (!IsChecksum(request)) return Reply(request);
            var bytes = new byte[129];
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = unknownLength ? new UnknownLengthContent(bytes) : new ByteArrayContent(bytes)
            };
        }));
        var source = new SecretArchiveSource(MissingPath(), Address, client, () => true);
        var error = Assert.ThrowsAsync<ArchiveIntegrityException>(async () => await source.LoadAsync(true, new ProgressLog(), CancellationToken.None));
        Assert.That(error!.Message, Does.Contain("size limit"));
    }

    [Test]
    public void CanceledCacheWritePreservesPreviousPair()
    {
        var path = MissingPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, _blob);
        File.WriteAllBytes(path + ArchiveIntegrity.Suffix, ArchiveIntegrity.CreateChecksum(_blob));
        using var client = new HttpClient(new Handler(_ => throw new AssertionException("No network allowed.")));
        var source = new SecretArchiveSource(path, Address, client);
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        try
        {
            Assert.ThrowsAsync<TaskCanceledException>(async () => await source.CacheValidatedAsync(new LoadedSecretArchive(_blob, true), cancel.Token));
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(_blob));
            ArchiveIntegrity.Verify(File.ReadAllBytes(path), File.ReadAllBytes(path + ArchiveIntegrity.Suffix));
            Assert.That(Directory.GetFiles(Path.GetDirectoryName(path)!), Has.Length.EqualTo(2), "No temporary files should remain.");
        }
        finally { Directory.Delete(Path.GetDirectoryName(path)!, true); }
    }

    [Test]
    public async Task TemporarySidecarFailureRetriesAndRetainsQueryOnSiblingUrl()
    {
        var checksumRequests = 0;
        var waits = 0;
        var address = new Uri("https://example.invalid/Secrets.aqsecrets?version=1");
        using var client = new HttpClient(new Handler(request =>
        {
            if (!IsChecksum(request)) return Reply(request);
            Assert.That(request.RequestUri!.Query, Is.EqualTo("?version=1"));
            return ++checksumRequests == 1 ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) : Reply(request, ArchiveIntegrity.CreateChecksum(_blob));
        }));
        var source = new SecretArchiveSource(MissingPath(), address, client, () => true, _ => { waits++; return Task.CompletedTask; });
        Assert.That((await source.LoadAsync(true, new ProgressLog(), CancellationToken.None)).Checksum, Is.Not.Null);
        Assert.That(waits, Is.EqualTo(1));
    }

    private static bool IsChecksum(HttpRequestMessage request) => request.RequestUri!.AbsolutePath.EndsWith(ArchiveIntegrity.Suffix);
    private HttpResponseMessage Reply(HttpRequestMessage request, byte[]? checksum = null, byte[]? blob = null) =>
        IsChecksum(request) && checksum is null ? new HttpResponseMessage(HttpStatusCode.NotFound) :
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(IsChecksum(request) ? checksum! : blob ?? _blob) };

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
    private sealed class UnknownLengthContent : HttpContent
    {
        private readonly byte[] _bytes;
        public UnknownLengthContent(byte[] bytes) => _bytes = bytes;
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => stream.WriteAsync(_bytes, 0, _bytes.Length);
        protected override Task<Stream> CreateContentReadStreamAsync() => Task.FromResult<Stream>(new MemoryStream(_bytes, false));
    }
}
