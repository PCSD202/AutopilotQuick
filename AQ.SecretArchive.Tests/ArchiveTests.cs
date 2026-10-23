using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using AutopilotQuick;
using NUnit.Framework;
#if NET10_0_OR_GREATER
using AQ.SecretArchive.Tool;
using System.Security.AccessControl;
#endif

namespace AQ.SecretArchive.Tests;

[TestFixture]
public class ArchiveTests
{
    private const string Password = "test-only passphrase 1234";

    [Test]
    public void RoundTripPreservesBinaryUnicodeAndEmptyFilesAndHidesNames()
    {
        using var input = Sample();
        var blob = SecretArchive.Encrypt(input, Password);
        Assert.That(Encoding.UTF8.GetString(blob), Does.Not.Contain("credentials.json").And.Not.Contain("synthetic-secret"));
        using var output = SecretArchive.Decrypt(blob, Password);
        Assert.That(output.ReadBytes("binary.bin"), Is.EqualTo(new byte[] { 0, 255, 42 }));
        Assert.That(output.ReadText("credentials.json"), Is.EqualTo("synthetic-secret λ 🐈"));
        Assert.That(output.ReadBytes("empty.txt"), Is.Empty);
    }

    [Test]
    public void EncryptingTwiceAndRekeyingProduceDifferentArchives()
    {
        using var input = Sample();
        var first = SecretArchive.Encrypt(input, Password);
        Assert.That(SecretArchive.Encrypt(input, Password), Is.Not.EqualTo(first));
        using var opened = SecretArchive.Decrypt(first, Password);
        var rekeyed = SecretArchive.Encrypt(opened, "different test passphrase");
        Assert.Throws<CryptographicException>(() => SecretArchive.Decrypt(rekeyed, Password));
        using var newContents = SecretArchive.Decrypt(rekeyed, "different test passphrase");
        Assert.That(newContents.ReadText("credentials.json"), Is.EqualTo(input.ReadText("credentials.json")));
    }

    [Test]
    public void WrongPasswordAndTamperingFailBeforeReturningContents()
    {
        using var input = Sample();
        var blob = SecretArchive.Encrypt(input, Password);
        Assert.Throws<CryptographicException>(() => SecretArchive.Decrypt(blob, "a wrong test passphrase"));
        foreach (var offset in new[] { 16, 32, 44, blob.Length - 1 })
        {
            var damaged = blob.ToArray();
            damaged[offset] ^= 1;
            Assert.Throws<CryptographicException>(() => SecretArchive.Decrypt(damaged, Password));
        }
        foreach (var offset in new[] { 0, 8, 12 })
        {
            var damaged = blob.ToArray();
            damaged[offset] ^= 1;
            Assert.Throws<InvalidDataException>(() => SecretArchive.Decrypt(damaged, Password));
        }
        Assert.Throws<InvalidDataException>(() => SecretArchive.Decrypt(blob[..^1], Password));
        Assert.Throws<InvalidDataException>(() => SecretArchive.Decrypt(blob.Concat(new byte[] { 0 }).ToArray(), Password));
    }

    [TestCase("../escape")]
    [TestCase("/absolute")]
    [TestCase("C:/absolute")]
    [TestCase("folder\\file")]
    [TestCase("file:stream")]
    [TestCase("NUL.txt")]
    [TestCase("COM1")]
    [TestCase("folder/../file")]
    [TestCase("folder//file")]
    [TestCase("file.")]
    [TestCase("file ")]
    public void RejectsNamesThatCouldEscapeOrAliasWindowsFiles(string name) =>
        Assert.Throws<InvalidDataException>(() => SecretArchive.ValidateName(name));

    [Test]
    public void RejectsCaseDuplicatesAndFileDirectoryCollisions()
    {
        using var input = Sample();
        Assert.Throws<InvalidDataException>(() => input.Add("BINARY.BIN", new byte[] { 1 }));
        Assert.Throws<InvalidDataException>(() => input.Add("binary.bin/child", new byte[] { 1 }));
        input.Add("folder/file", new byte[] { 1 });
        Assert.Throws<InvalidDataException>(() => input.Add("folder", new byte[] { 1 }));
        input.Dispose();
        Assert.Throws<ObjectDisposedException>(() => input.ReadBytes("binary.bin"));
    }

    [Test]
    public void NestedZipRejectsTraversalAndDuplicateEntriesAndAllowsDirectories()
    {
        foreach (var names in new[] { new[] { "../escape" }, new[] { "same", "SAME" } })
        {
            using var memory = new MemoryStream();
            using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, true))
                foreach (var name in names) zip.CreateEntry(name);
            memory.Position = 0;
            Assert.Throws<InvalidDataException>(() => SecretArchive.ReadZip(memory));
        }
        using var valid = new MemoryStream();
        using (var zip = new ZipArchive(valid, ZipArchiveMode.Create, true))
        {
            zip.CreateEntry("folder/");
            zip.CreateEntry("folder/file");
        }
        valid.Position = 0;
        using var contents = SecretArchive.ReadZip(valid);
        Assert.That(contents.Names, Is.EqualTo(new[] { "folder/file" }));
    }

    [Test]
    public void RejectsPasswordsBelowMinimumWhitespaceAndTooManyEntries()
    {
        using var input = Sample();
        Assert.Throws<ArgumentException>(() => SecretArchive.Encrypt(input, "short"));
        Assert.Throws<ArgumentException>(() => SecretArchive.Encrypt(input, new string(' ', 20)));
        using var many = new ArchiveContents();
        for (var i = 0; i < SecretArchive.MaximumEntries; i++) many.Add("file" + i, Array.Empty<byte>());
        Assert.Throws<InvalidDataException>(() => many.Add("one-too-many", Array.Empty<byte>()));
    }

    [Test]
    public void SixCharacterPasswordRoundTripsAndStillRejectsWrongPassword()
    {
        using var input = Sample();
        var blob = SecretArchive.Encrypt(input, "aB3!xZ");
        using var output = SecretArchive.Decrypt(blob, "aB3!xZ");
        Assert.That(output.ReadText("credentials.json"), Is.EqualTo(input.ReadText("credentials.json")));
        Assert.Throws<CryptographicException>(() => SecretArchive.Decrypt(blob, "aB3!xY"));
        Assert.Throws<ArgumentException>(() => SecretArchive.Decrypt(blob, "short"));
    }

    [Test]
    public void LargerArchiveAndNestedBundleRoundTripBeyondOld64MiBLimit()
    {
        var large = new byte[65 * 1024 * 1024];
        large[0] = 42;
        large[^1] = 255;
        using var input = new ArchiveContents();
        input.Add("large.bin", large);
        var blob = SecretArchive.Encrypt(input, Password);
        using var output = SecretArchive.Decrypt(blob, Password);
        Assert.That(output.TotalBytes, Is.EqualTo(large.Length));
        Assert.That(SHA256.HashData(output.ReadBytes("large.bin")), Is.EqualTo(SHA256.HashData(large)));
        using var nested = new MemoryStream();
        using (var zip = new ZipArchive(nested, ZipArchiveMode.Create, true))
        {
            using var entry = zip.CreateEntry("large.bin", CompressionLevel.Fastest).Open();
            entry.Write(large);
        }
        nested.Position = 0;
        using var expanded = SecretArchive.ReadZip(nested);
        Assert.That(expanded.TotalBytes, Is.EqualTo(large.Length));
    }

    [Test]
    public void AppStartsWithoutGithubOrGraphCredentialsAndRejectsInvalidOptionalGraphOrMissingConfig()
    {
        using var valid = AppSample();
        Assert.That(valid.Names, Does.Not.Contain(SecretNames.Graph));
        Assert.That(valid.Names, Does.Not.Contain("GithubCreds.json"));
        AppSecrets.Initialize(valid);
        Assert.That(AppSecrets.ReadText(SecretNames.BiosPassword), Is.EqualTo("test-only-bios-password"));
        AppSecrets.Clear();
        using var old = AppSample(includeGraph: true, encryptedGraph: true);
        Assert.Throws<InvalidDataException>(() => AppSecrets.Initialize(old));
        Assert.Throws<InvalidOperationException>(() => AppSecrets.ReadText(SecretNames.Graph));
        using var missing = Sample();
        Assert.Throws<InvalidDataException>(() => AppSecrets.Initialize(missing));
    }

    [Test]
    public void AppSuppressesParserValuesInErrors()
    {
        using var input = new ArchiveContents();
        input.Add(SecretNames.GroupMan, Encoding.UTF8.GetBytes("{\"APIKEY\":synthetic-secret}"));
        var error = Assert.Throws<InvalidDataException>(() => AppSecrets.Initialize(input));
        Assert.That(error!.ToString(), Does.Not.Contain("synthetic-secret"));
    }

    [Test]
    public void SharedFixtureCanBeWrittenOrReadAcrossFrameworks()
    {
        var path = Environment.GetEnvironmentVariable("AQ_TEST_FIXTURE");
        if (path is null) Assert.Ignore("Cross-framework fixture is enabled by the validation script.");
        if (Environment.GetEnvironmentVariable("AQ_TEST_WRITE_FIXTURE") == "1")
        {
            using var input = Sample();
            File.WriteAllBytes(path!, SecretArchive.Encrypt(input, Password));
        }
        else
        {
            using var input = SecretArchive.Decrypt(File.ReadAllBytes(path!), Password);
            Assert.That(input.ReadText("credentials.json"), Is.EqualTo("synthetic-secret λ 🐈"));
        }
    }

#if NET10_0_OR_GREATER
    [Test]
    public void ToolPreservesSourcesRefusesOverwriteAndExtractsToPrivateNewFolder()
    {
        var root = Path.Combine(Path.GetTempPath(), "aq-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var source = Path.Combine(root, "source");
            Directory.CreateDirectory(Path.Combine(source, "nested"));
            File.WriteAllText(Path.Combine(source, "nested", "creds.json"), "synthetic-only");
            using var contents = ArchiveFiles.PackFolder(source);
            var archive = Path.Combine(root, "sample.aqsecrets");
            ArchiveFiles.WriteNew(archive, SecretArchive.Encrypt(contents, Password));
            Assert.Throws<ToolOperationException>(() => ArchiveFiles.WriteNew(archive, new byte[] { 0 }));
            Assert.That(File.ReadAllText(Path.Combine(source, "nested", "creds.json")), Is.EqualTo("synthetic-only"));
            var output = Path.Combine(root, "output");
            ArchiveFiles.ExtractNew(contents, output + Path.DirectorySeparatorChar);
            Assert.That(File.ReadAllText(Path.Combine(output, "nested", "creds.json")), Is.EqualTo("synthetic-only"));
            Assert.Throws<ToolOperationException>(() => ArchiveFiles.ExtractNew(contents, output));
            if (OperatingSystem.IsWindows())
            {
                var acl = new DirectoryInfo(output).GetAccessControl();
                Assert.That(acl.AreAccessRulesProtected, Is.True);
                Assert.That(acl.GetAccessRules(true, true, typeof(System.Security.Principal.SecurityIdentifier)).Count, Is.EqualTo(1));
            }
        }
        finally { Directory.Delete(root, true); }
    }

    [Test]
    public void SizePreflightAcceptsCurrentBundleSizeAndReportsOversizeWithoutReadingContents()
    {
        var root = Path.Combine(Path.GetTempPath(), "aq-size-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var filePath = Path.Combine(root, "DellBiosSettings.zip");
            using (var file = File.Create(filePath)) file.SetLength(119372451);
            using (var locked = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                var files = ArchiveFiles.InspectFolder(root);
                Assert.That(files.Single().Length, Is.EqualTo(119372451));
            }
            using (var file = File.OpenWrite(filePath)) file.SetLength(SecretArchive.MaximumContentBytes + 1L);
            var error = Assert.Throws<ToolOperationException>(() => ArchiveFiles.InspectFolder(root));
            Assert.That(error!.Message, Does.Contain("DellBiosSettings.zip").And.Contain("256 MiB"));
        }
        finally { Directory.Delete(root, true); }
    }

    [Test]
    public void ErrorsExplainKnownOperationalFailuresWithoutEchoingRawExceptions()
    {
        Assert.That(ToolErrors.Describe(new UnauthorizedAccessException("synthetic-secret")), Does.Contain("Access denied").And.Not.Contain("synthetic-secret"));
        Assert.That(ToolErrors.Describe(new Exception("synthetic-secret")), Does.Not.Contain("synthetic-secret"));
        Assert.That(ToolErrors.Describe(new CryptographicException("synthetic-secret")), Does.Contain("passphrase").And.Not.Contain("synthetic-secret"));
        Assert.That(ToolErrors.Describe(new ToolOperationException("Output already exists.")), Is.EqualTo("Output already exists."));
    }
#endif

    private static ArchiveContents Sample()
    {
        var contents = new ArchiveContents();
        contents.Add("credentials.json", Encoding.UTF8.GetBytes("synthetic-secret λ 🐈"));
        contents.Add("binary.bin", new byte[] { 0, 255, 42 });
        contents.Add("empty.txt", Array.Empty<byte>());
        return contents;
    }

    private static ArchiveContents AppSample(bool includeGraph = false, bool encryptedGraph = false)
    {
        var contents = new ArchiveContents();
        void Add(string name, string value) => contents.Add(name, Encoding.UTF8.GetBytes(value));
        foreach (var name in new[] { SecretNames.GroupMan, SecretNames.TakeHome, SecretNames.TakeHomeLogger })
            Add(name, "{\"ApiKey\":\"test-only\",\"Url\":\"https://example.invalid/\"}");
        if (includeGraph)
            Add(SecretNames.Graph, "{\"AppID\":\"test\",\"AppSecret\":\"test\",\"TenantID\":\"test\",\"Encrypted\":" + (encryptedGraph ? "true" : "false") + "}");
        Add(SecretNames.AzureLogs, "{\"ConnectionString\":\"test-only\"}");
        Add(SecretNames.AppInsights, "{\"ConnectionString\":\"test-only\"}");
        Add(SecretNames.BiosPassword, "test-only-bios-password");
        Add(SecretNames.WifiPackage, "test-only-package");
        Add(SecretNames.BiosUpdatePackage, "test-only-package");
        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, true))
        {
            foreach (var name in new[] { "cctk.exe", "LaptopBiosSettings.cmd", "DesktopBiosSettings.cmd" })
            {
                using var stream = zip.CreateEntry(name).Open();
                stream.Write(new byte[] { 0 });
            }
        }
        contents.Add(SecretNames.DellBiosSettings, memory.ToArray());
        return contents;
    }
}
