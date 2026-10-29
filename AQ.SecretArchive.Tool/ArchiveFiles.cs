using System.Security.Cryptography;
using System.Security.AccessControl;
using System.Security.Principal;

namespace AQ.SecretArchive.Tool;

public static class ArchiveFiles
{
    public static ArchiveContents PackFolder(string source)
    {
        var root = Path.GetFullPath(source);
        var files = InspectFolder(root);
        var contents = new ArchiveContents();
        try
        {
            foreach (var item in files)
            {
                var path = item.Path;
                RejectLinks(path);
                using var file = File.OpenRead(path);
                if (file.Length > SecretArchive.MaximumContentBytes - contents.TotalBytes)
                    throw SizeError(Path.GetRelativePath(root, path), file.Length, contents.TotalBytes);
                var bytes = new byte[(int)file.Length];
                try
                {
                    SecretArchive.ReadExactly(file, bytes);
                    if (file.ReadByte() != -1) throw new IOException("Source file changed while reading.");
                    contents.Add(Path.GetRelativePath(root, path).Replace('\\', '/'), bytes);
                }
                finally { CryptographicOperations.ZeroMemory(bytes); }
            }
            return contents;
        }
        catch { contents.Dispose(); throw; }
    }

    public static IReadOnlyList<SourceFile> InspectFolder(string source)
    {
        var root = Path.GetFullPath(source);
        RejectLinks(root);
        var files = new List<SourceFile>();
        long totalBytes = 0;
        Inspect(root);
        if (files.Count == 0) throw new ToolOperationException("Source folder contains no files.");
        return files;

        void Inspect(string folder)
        {
            foreach (var path in Directory.EnumerateFileSystemEntries(folder))
            {
                RejectLinks(path);
                var name = Path.GetRelativePath(root, path).Replace('\\', '/');
                try { SecretArchive.ValidateName(name); }
                catch (InvalidDataException) { throw new ToolOperationException($"Unsupported archive filename: {name}"); }
                if (Directory.Exists(path)) { Inspect(path); continue; }
                var length = new FileInfo(path).Length;
                if (length > SecretArchive.MaximumContentBytes - totalBytes) throw SizeError(name, length, totalBytes);
                if (files.Count >= SecretArchive.MaximumEntries)
                    throw new ToolOperationException($"Source contains more than {SecretArchive.MaximumEntries:N0} files.");
                totalBytes += length;
                files.Add(new SourceFile(path, length));
            }
        }
    }

    private static ToolOperationException SizeError(string name, long length, long previousBytes) => new(
        $"Cannot include {name} ({length / 1048576d:F2} MiB): total content would be {(length + previousBytes) / 1048576d:F2} MiB. " +
        $"The limit is {SecretArchive.MaximumContentBytes / 1048576:N0} MiB.");

    public sealed record SourceFile(string Path, long Length);

    public static ArchiveContents Unlock(string path)
    {
        var blob = ReadVerified(path, out _);
        var password = PasswordPrompt.Read();
        try { return SecretArchive.Decrypt(blob, password); }
        finally { Array.Clear(password); }
    }

    public static byte[] ReadVerified(string path, out bool hasChecksum)
    {
        using var file = File.OpenRead(path);
        var blob = SecretArchive.ReadBlob(file);
        byte[]? checksum = null;
        hasChecksum = File.Exists(path + ArchiveIntegrity.Suffix);
        if (hasChecksum)
        {
            using var sidecar = File.OpenRead(path + ArchiveIntegrity.Suffix);
            checksum = ArchiveIntegrity.ReadChecksum(sidecar);
        }
        ArchiveIntegrity.Verify(blob, checksum);
        return blob;
    }

    public static void ValidateNewArchive(string path)
    {
        ValidateNewFile(path);
        ValidateNewFile(path + ArchiveIntegrity.Suffix);
    }

    public static void WriteNewArchive(string path, byte[] blob)
    {
        ValidateNewArchive(path);
        var checksumPath = path + ArchiveIntegrity.Suffix;
        WriteNew(checksumPath, ArchiveIntegrity.CreateChecksum(blob));
        try { WriteNew(path, blob); }
        catch
        {
            File.Delete(checksumPath); // Only the sidecar created by this operation is removed.
            throw;
        }
    }

    public static void ValidateNewFile(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (File.Exists(fullPath) || Directory.Exists(fullPath))
            throw new ToolOperationException("Output already exists. Choose a new output archive; existing archives are preserved.");
        RejectLinks(Path.GetDirectoryName(fullPath)!);
    }

    public static void WriteNew(string path, byte[] blob)
    {
        var fullPath = Path.GetFullPath(path);
        ValidateNewFile(fullPath);
        var temporary = Path.Combine(Path.GetDirectoryName(fullPath)!, $".aq-{Guid.NewGuid():N}.tmp");
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                file.Write(blob);
                file.Flush(true);
            }
            File.Move(temporary, fullPath, false);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static void ExtractNew(ArchiveContents contents, string destination)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(destination));
        if (Directory.Exists(root) || File.Exists(root))
            throw new ToolOperationException("Extraction requires a new output folder. Choose a folder that does not already exist.");
        RejectLinks(Path.GetDirectoryName(root)!);
        if (OperatingSystem.IsWindows())
        {
            var security = new DirectorySecurity();
            security.SetAccessRuleProtection(true, false);
            security.AddAccessRule(new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!, FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            new DirectoryInfo(root).Create(security);
        }
        else
        {
            Directory.CreateDirectory(root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        foreach (var name in contents.Names)
        {
            SecretArchive.ValidateName(name);
            var path = Path.GetFullPath(Path.Combine(root, name.Replace('/', Path.DirectorySeparatorChar)));
            if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Entry escapes the output folder.");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            RejectLinks(Path.GetDirectoryName(path)!);
            using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            contents.CopyTo(name, file);
        }
    }

    private static void RejectLinks(string path)
    {
        for (var current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new ToolOperationException("Symbolic links and junctions are not supported. Choose a regular source/output folder.");
    }
}
