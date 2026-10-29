using System.Security.Cryptography;
using System.Text;

namespace AQ.SecretArchive;

/// <summary>Optional transport checksum alongside the unchanged v1 blob. AES-GCM remains the authentication boundary.</summary>
public static class ArchiveIntegrity
{
    public const string Suffix = ".sha256";
    public const int MaximumChecksumBytes = 128;

    public static byte[] CreateChecksum(ReadOnlySpan<byte> blob)
    {
        Verify(blob);
        return Encoding.ASCII.GetBytes(Convert.ToHexString(SHA256.HashData(blob)) + "\n");
    }

    public static void Verify(ReadOnlySpan<byte> blob, byte[]? checksum = null)
    {
        try { SecretArchive.ValidateBlob(blob); }
        catch (InvalidDataException)
        {
            throw new ArchiveIntegrityException("The encrypted bundle is incomplete, damaged, or in an unsupported format. Download a fresh bundle.");
        }
        if (checksum is null) return; // Legacy bundles still require AES-GCM authentication at unlock.
        if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(blob), ParseChecksum(checksum)))
            throw new ArchiveIntegrityException("The encrypted bundle does not match its checksum. It may be damaged, or the bundle and checksum may be different versions. Download a matching pair.");
    }

    public static byte[] ParseChecksum(ReadOnlySpan<byte> checksum)
    {
        if (checksum.Length > MaximumChecksumBytes)
            throw InvalidChecksum();
        var text = Encoding.ASCII.GetString(checksum).Trim();
        if (text.Length != 64) throw InvalidChecksum();
        try { return Convert.FromHexString(text); }
        catch (FormatException) { throw InvalidChecksum(); }
    }

    public static byte[] ReadChecksum(Stream input)
    {
        using var buffer = new MemoryStream();
        var bytes = new byte[MaximumChecksumBytes + 1];
        int count;
        while ((count = input.Read(bytes, 0, bytes.Length - (int)buffer.Length)) > 0)
        {
            buffer.Write(bytes, 0, count);
            if (buffer.Length > MaximumChecksumBytes) throw InvalidChecksum();
        }
        var checksum = buffer.ToArray();
        ParseChecksum(checksum);
        return checksum;
    }

    private static ArchiveIntegrityException InvalidChecksum() => new(
        "The bundle checksum file is invalid. Publish a matching Secrets.aqsecrets.sha256 containing only the SHA-256 hex digest.");
}

/// <summary>Only safe, authored integrity diagnostics are included in this exception.</summary>
public sealed class ArchiveIntegrityException : Exception
{
    public ArchiveIntegrityException(string message) : base(message) { }
}
