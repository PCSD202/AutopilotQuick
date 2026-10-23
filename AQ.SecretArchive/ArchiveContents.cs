using System.Security.Cryptography;
using System.Text;

namespace AQ.SecretArchive;

/// <summary>In-memory plaintext entries. Dispose clears the owned byte buffers; returned strings cannot be erased.</summary>
public sealed class ArchiveContents : IDisposable
{
    private readonly Dictionary<string, byte[]> _entries = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;
    public int TotalBytes { get; private set; }
    public IReadOnlyList<string> Names { get { CheckDisposed(); return _entries.Keys.OrderBy(x => x, StringComparer.Ordinal).ToArray(); } }

    public void Add(string name, ReadOnlySpan<byte> bytes)
    {
        CheckDisposed();
        SecretArchive.ValidateName(name);
        if (_entries.Count >= SecretArchive.MaximumEntries || bytes.Length > SecretArchive.MaximumContentBytes - TotalBytes)
            throw new InvalidDataException("Archive content exceeds the size limit.");
        if (_entries.Keys.Any(existing => name.Equals(existing, StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith(existing + "/", StringComparison.OrdinalIgnoreCase) || existing.StartsWith(name + "/", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Duplicate or conflicting archive entry.");
        _entries.Add(name, bytes.ToArray());
        TotalBytes += bytes.Length;
    }

    public byte[] ReadBytes(string name) => Find(name).ToArray();
    public string ReadText(string name) => new UTF8Encoding(false, true).GetString(Find(name)).TrimStart('\uFEFF');
    public void CopyTo(string name, Stream destination) => destination.Write(Find(name));

    private byte[] Find(string name)
    {
        CheckDisposed();
        return _entries.TryGetValue(name, out var bytes) ? bytes : throw new InvalidDataException($"Required archive entry is missing: {name}");
    }

    private void CheckDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(ArchiveContents));
    }

    public void Dispose()
    {
        if (_disposed) return;
        foreach (var bytes in _entries.Values) CryptographicOperations.ZeroMemory(bytes);
        _entries.Clear();
        TotalBytes = 0;
        _disposed = true;
    }
}
