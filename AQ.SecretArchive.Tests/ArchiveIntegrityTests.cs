using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using NUnit.Framework;
#if NET10_0_OR_GREATER
using AQ.SecretArchive.Tool;
#endif

namespace AQ.SecretArchive.Tests;

[TestFixture]
public class ArchiveIntegrityTests
{
    private byte[] _blob = null!;

    [OneTimeSetUp]
    public void CreateSyntheticArchive()
    {
        using var contents = new ArchiveContents();
        contents.Add("sample.txt", Encoding.UTF8.GetBytes("synthetic-only"));
        _blob = SecretArchive.Encrypt(contents, "test-only-password");
    }

    [Test]
    public void SidecarMatchesIndependentSha256AndDoesNotAlterV1Layout()
    {
        var checksum = ArchiveIntegrity.CreateChecksum(_blob);
        Assert.That(Encoding.ASCII.GetString(checksum).Trim(), Is.EqualTo(Convert.ToHexString(SHA256.HashData(_blob))));
        Assert.That(Encoding.ASCII.GetString(_blob, 0, 8), Is.EqualTo("AQSECR01"));
        Assert.That(_blob.Length, Is.EqualTo(44 + BinaryPrimitives.ReadInt32LittleEndian(_blob.AsSpan(12, 4)) + 16),
            "Old clients require this exact v1 size; a footer would break them.");
        ArchiveIntegrity.Verify(_blob, checksum);
        // Independently apply the v2.0.3 reader's cryptographic layout, without the new integrity API.
        var key = Rfc2898DeriveBytes.Pbkdf2("test-only-password", _blob.AsSpan(16, 16), 600_000, HashAlgorithmName.SHA256, 32);
        var plaintext = new byte[_blob.Length - 60];
        try
        {
#if NET8_0_OR_GREATER
            using var aes = new AesGcm(key, 16);
#else
            using var aes = new AesGcm(key);
#endif
            aes.Decrypt(_blob.AsSpan(32, 12), _blob.AsSpan(44, plaintext.Length), _blob.AsSpan(_blob.Length - 16), plaintext, _blob.AsSpan(0, 44));
            using var zip = new MemoryStream(plaintext);
            using var contents = SecretArchive.ReadZip(zip);
            Assert.That(contents.ReadText("sample.txt"), Is.EqualTo("synthetic-only"));
        }
        finally { CryptographicOperations.ZeroMemory(key); CryptographicOperations.ZeroMemory(plaintext); }
    }

    [TestCase(0)]
    [TestCase(8)]
    [TestCase(12)]
    [TestCase(16)]
    [TestCase(32)]
    [TestCase(44)]
    [TestCase(-1)]
    public void DetectsCorruptionBeforeAnyPasswordIsProvided(int offset)
    {
        var damaged = _blob.ToArray();
        damaged[offset < 0 ? damaged.Length - 1 : offset] ^= 1;
        Assert.Throws<ArchiveIntegrityException>(() => ArchiveIntegrity.Verify(damaged, ArchiveIntegrity.CreateChecksum(_blob)));
    }

    [Test]
    public void LegacyChecksRejectTruncatedAndExtraBytesWithoutChecksum()
    {
        ArchiveIntegrity.Verify(_blob);
        Assert.Throws<ArchiveIntegrityException>(() => ArchiveIntegrity.Verify(_blob[..^1]));
        Assert.Throws<ArchiveIntegrityException>(() => ArchiveIntegrity.Verify(_blob.Concat(new byte[] { 0 }).ToArray()));
        Assert.Throws<ArchiveIntegrityException>(() => ArchiveIntegrity.Verify(Array.Empty<byte>()));
    }

    [Test]
    public void ChecksumDoesNotReplaceAuthenticationOrProveCorrectPassword()
    {
        ArchiveIntegrity.Verify(_blob, ArchiveIntegrity.CreateChecksum(_blob));
        Assert.Throws<CryptographicException>(() => SecretArchive.Decrypt(_blob, "incorrect-test-password"));
        var damaged = _blob.ToArray();
        damaged[44] ^= 1;
        // An attacker who can replace both files can recompute a checksum, but still cannot authenticate the ciphertext.
        ArchiveIntegrity.Verify(damaged, ArchiveIntegrity.CreateChecksum(damaged));
        Assert.Throws<CryptographicException>(() => SecretArchive.Decrypt(damaged, "test-only-password"));
    }

    [Test]
    public void SupportsHexCaseAndLineEndingsButRejectsMalformedAndOversizedSidecars()
    {
        var lower = Encoding.ASCII.GetString(ArchiveIntegrity.CreateChecksum(_blob)).Trim().ToLowerInvariant() + "\r\n";
        ArchiveIntegrity.Verify(_blob, Encoding.ASCII.GetBytes(lower));
        foreach (var invalid in new[] { "", new string('z', 64), new string('0', 63), new string('0', 64) + "  filename", new string(' ', 129) })
            Assert.Throws<ArchiveIntegrityException>(() => ArchiveIntegrity.ParseChecksum(Encoding.ASCII.GetBytes(invalid)));
        using var oversized = new MemoryStream(new byte[129]);
        Assert.Throws<ArchiveIntegrityException>(() => ArchiveIntegrity.ReadChecksum(oversized));
        using var valid = new MemoryStream(ArchiveIntegrity.CreateChecksum(_blob));
        Assert.That(ArchiveIntegrity.ReadChecksum(valid), Is.EqualTo(ArchiveIntegrity.CreateChecksum(_blob)));
    }

#if NET10_0_OR_GREATER
    [Test]
    public void ToolCreatesMatchingPairAndPreservesExistingOutputs()
    {
        var root = Path.Combine(Path.GetTempPath(), "aq-integrity-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "Secrets.aqsecrets");
        try
        {
            ArchiveFiles.WriteNewArchive(path, _blob);
            Assert.That(ArchiveFiles.ReadVerified(path, out var hasChecksum), Is.EqualTo(_blob));
            Assert.That(hasChecksum, Is.True);
            Assert.Throws<ToolOperationException>(() => ArchiveFiles.WriteNewArchive(path, _blob));
            var other = Path.Combine(root, "other.aqsecrets");
            File.WriteAllText(other + ArchiveIntegrity.Suffix, "preserve-existing-checksum");
            Assert.Throws<ToolOperationException>(() => ArchiveFiles.WriteNewArchive(other, _blob));
            Assert.That(File.Exists(other), Is.False);
            Assert.That(File.ReadAllText(other + ArchiveIntegrity.Suffix), Is.EqualTo("preserve-existing-checksum"));
        }
        finally { Directory.Delete(root, true); }
    }
#endif
}
