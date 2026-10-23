namespace AQ.SecretArchive.Tool;

/// <summary>Only explicitly authored operational messages may be displayed verbatim.</summary>
public sealed class ToolOperationException(string message) : Exception(message);

public static class ToolErrors
{
    public static string Describe(Exception error) => error switch
    {
        ToolOperationException => error.Message,
        UnauthorizedAccessException => "Access denied. Check permissions on the source and output folder.",
        DirectoryNotFoundException => "A directory was not found. Check the source and output parent folder.",
        FileNotFoundException => "A file was not found. Check the archive path and source files.",
        System.Security.Cryptography.CryptographicException => "Incorrect passphrase or damaged archive.",
        OperationCanceledException => "Operation canceled.",
        InvalidDataException => "Invalid archive data: check the format, size limits, filenames, and duplicate entries.",
        IOException => "A file operation failed. Check permissions, available disk space, and whether files are in use.",
        _ => "Operation failed. Check the command arguments and archive configuration."
    };
}
