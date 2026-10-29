using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace AQ.SecretArchive;

/// <summary>Versioned, password-encrypted ZIP. All metadata is authenticated before ZIP parsing.</summary>
public static class SecretArchive
{
    public const int Iterations = 600_000;
    public const int MinimumPasswordLength = 6;
    public const int MaximumBlobBytes = 272 * 1024 * 1024;
    public const int MaximumContentBytes = 256 * 1024 * 1024;
    public const int MaximumEntries = 1024;
    private const int HeaderBytes = 44;
    private const int TagBytes = 16;
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("AQSECR01");

    public static byte[] Encrypt(ArchiveContents contents, ReadOnlySpan<char> password)
    {
        ValidatePassword(password);
        using var payload = new MemoryStream();
        byte[]? key = null;
        try
        {
            using (var zip = new ZipArchive(payload, ZipArchiveMode.Create, true))
            {
                foreach (var name in contents.Names)
                {
                    using var entry = zip.CreateEntry(name, CompressionLevel.NoCompression).Open();
                    contents.CopyTo(name, entry);
                }
            }
            if (payload.Length > MaximumBlobBytes - HeaderBytes - TagBytes)
                throw new InvalidDataException("Archive exceeds the size limit.");
            var blob = new byte[HeaderBytes + (int)payload.Length + TagBytes];
            Magic.CopyTo(blob, 0);
            BinaryPrimitives.WriteInt32LittleEndian(blob.AsSpan(8, 4), Iterations);
            BinaryPrimitives.WriteInt32LittleEndian(blob.AsSpan(12, 4), (int)payload.Length);
            RandomNumberGenerator.Fill(blob.AsSpan(16, 16));
            RandomNumberGenerator.Fill(blob.AsSpan(32, 12));
            key = Rfc2898DeriveBytes.Pbkdf2(password, blob.AsSpan(16, 16), Iterations, HashAlgorithmName.SHA256, 32);
            using var aes = CreateAes(key);
            aes.Encrypt(blob.AsSpan(32, 12), payload.GetBuffer().AsSpan(0, (int)payload.Length),
                blob.AsSpan(HeaderBytes, (int)payload.Length), blob.AsSpan(blob.Length - TagBytes), blob.AsSpan(0, HeaderBytes));
            return blob;
        }
        finally
        {
            if (key is not null) CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(payload.GetBuffer());
        }
    }

    public static ArchiveContents Decrypt(ReadOnlySpan<byte> blob, ReadOnlySpan<char> password)
    {
        ValidatePassword(password);
        ValidateBlob(blob);
        var key = Rfc2898DeriveBytes.Pbkdf2(password, blob.Slice(16, 16), Iterations, HashAlgorithmName.SHA256, 32);
        var plaintext = new byte[blob.Length - HeaderBytes - TagBytes];
        try
        {
            using var aes = CreateAes(key);
            try
            {
                aes.Decrypt(blob.Slice(32, 12), blob.Slice(HeaderBytes, plaintext.Length), blob[^TagBytes..],
                    plaintext, blob[..HeaderBytes]);
            }
            catch (CryptographicException)
            {
                throw new CryptographicException("Incorrect password or damaged secret archive.");
            }
            using var input = new MemoryStream(plaintext, false);
            return ReadZip(input);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    /// <summary>Check the public version, limits and exact length without a password. This does not authenticate the archive.</summary>
    public static void ValidateBlob(ReadOnlySpan<byte> blob)
    {
        if (blob.Length < HeaderBytes + TagBytes || blob.Length > MaximumBlobBytes ||
            !blob[..8].SequenceEqual(Magic))
            throw new InvalidDataException("Unsupported or invalid secret archive.");
        // Pin the KDF cost for this version so an unauthenticated header cannot cause unbounded work.
        if (BinaryPrimitives.ReadInt32LittleEndian(blob.Slice(8, 4)) != Iterations ||
            BinaryPrimitives.ReadInt32LittleEndian(blob.Slice(12, 4)) != blob.Length - HeaderBytes - TagBytes)
            throw new InvalidDataException("Invalid secret archive header.");
    }

    /// <summary>Read a bounded ZIP into memory; reject unsafe names, collisions, and misleading lengths.</summary>
    public static ArchiveContents ReadZip(Stream input)
    {
        using var zip = new ZipArchive(input, ZipArchiveMode.Read, true);
        var contents = new ArchiveContents();
        try
        {
            if (zip.Entries.Count > MaximumEntries)
                throw new InvalidDataException("Too many archive entries.");
            foreach (var entry in zip.Entries)
            {
                if (entry.FullName.EndsWith('/'))
                {
                    ValidateName(entry.FullName.TrimEnd('/'));
                    if (entry.Length != 0) throw new InvalidDataException("Invalid directory entry.");
                    continue;
                }
                ValidateName(entry.FullName);
                if (entry.Length > MaximumContentBytes - contents.TotalBytes)
                    throw new InvalidDataException("Archive content exceeds the size limit.");
                var bytes = new byte[(int)entry.Length];
                try
                {
                    using var stream = entry.Open();
                    ReadExactly(stream, bytes);
                    if (stream.ReadByte() != -1) throw new InvalidDataException("Invalid entry length.");
                    contents.Add(entry.FullName, bytes);
                }
                finally { CryptographicOperations.ZeroMemory(bytes); }
            }
            return contents;
        }
        catch { contents.Dispose(); throw; }
    }

    public static void ValidateName(string name)
    {
        if (string.IsNullOrEmpty(name) || name.Length > 240 || name.Contains('\\'))
            throw new InvalidDataException("Invalid archive entry name.");
        foreach (var segment in name.Split('/'))
        {
            var stem = segment.Split('.')[0];
            if (string.IsNullOrEmpty(segment) || segment is "." or ".." || segment.EndsWith('.') || segment.EndsWith(' ') ||
                segment.Any(c => c < 32 || "<>:\"|?*".Contains(c)) ||
                new[] { "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$" }.Contains(stem, StringComparer.OrdinalIgnoreCase) ||
                (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase) ||
                                     stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) && "123456789¹²³".Contains(stem[3])))
                throw new InvalidDataException("Unsafe archive entry name.");
        }
    }

    public static byte[] ReadBlob(Stream stream)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int count;
        while ((count = stream.Read(chunk, 0, Math.Min(chunk.Length, MaximumBlobBytes + 1 - (int)buffer.Length))) > 0)
        {
            buffer.Write(chunk, 0, count);
            if (buffer.Length > MaximumBlobBytes) throw new InvalidDataException("Archive exceeds the size limit.");
        }
        return buffer.ToArray();
    }

    private static void ValidatePassword(ReadOnlySpan<char> password)
    {
        if (password.Length < MinimumPasswordLength || password.Length > 1024 || password.IsWhiteSpace())
            throw new ArgumentException($"Use a passphrase of {MinimumPasswordLength} to 1024 characters.");
    }

    private static AesGcm CreateAes(byte[] key)
    {
#if NET8_0_OR_GREATER
        return new AesGcm(key, TagBytes);
#else
        return new AesGcm(key);
#endif
    }

    internal static void ReadExactly(Stream input, byte[] output)
    {
        var offset = 0;
        while (offset < output.Length)
        {
            var read = input.Read(output, offset, output.Length - offset);
            if (read == 0) throw new InvalidDataException("Truncated archive entry.");
            offset += read;
        }
    }
}
