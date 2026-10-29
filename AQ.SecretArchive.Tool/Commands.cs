using Spectre.Console;
using Spectre.Console.Cli;

namespace AQ.SecretArchive.Tool;

public sealed class CreateSettings : CommandSettings
{
    [CommandArgument(0, "<source-folder>")] public string Source { get; set; } = "";
    [CommandArgument(1, "<output-archive>")] public string Output { get; set; } = "";
}
public sealed class CheckSettings : CommandSettings
{
    [CommandArgument(0, "<source-folder>")] public string Source { get; set; } = "";
}
public sealed class CheckCommand : Command<CheckSettings>
{
    public override int Execute(CommandContext context, CheckSettings settings, CancellationToken cancellationToken)
    {
        var files = ArchiveFiles.InspectFolder(settings.Source);
        AnsiConsole.WriteLine($"Source size and filenames passed: {files.Count:N0} files, {files.Sum(x => x.Length) / 1048576d:F2} MiB. " +
            $"Content limit: {SecretArchive.MaximumContentBytes / 1048576:N0} MiB. File contents and app configuration were not checked.");
        return 0;
    }
}
public class ArchiveSettings : CommandSettings
{
    [CommandArgument(0, "<archive>")] public string Archive { get; set; } = "";
}
public sealed class ExtractSettings : ArchiveSettings
{
    [CommandArgument(1, "<new-output-folder>")] public string Output { get; set; } = "";
}
public sealed class RekeySettings : ArchiveSettings
{
    [CommandArgument(1, "<new-output-archive>")] public string Output { get; set; } = "";
}

public sealed class CreateCommand : Command<CreateSettings>
{
    public override int Execute(CommandContext context, CreateSettings settings, CancellationToken cancellationToken)
    {
        var source = Path.GetFullPath(settings.Source);
        var output = Path.GetFullPath(settings.Output);
        if (output.StartsWith(Path.TrimEndingDirectorySeparator(source) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new ToolOperationException("Output must be outside the source folder.");
        ArchiveFiles.ValidateNewArchive(output);
        using var contents = ArchiveFiles.PackFolder(source);
        var password = PasswordPrompt.Read(confirm: true);
        try { ArchiveFiles.WriteNewArchive(output, SecretArchive.Encrypt(contents, password)); }
        finally { Array.Clear(password); }
        AnsiConsole.WriteLine("Encrypted archive and .sha256 checksum created. Deploy both files. Source files were preserved.");
        return 0;
    }
}
public sealed class ListCommand : Command<ArchiveSettings>
{
    public override int Execute(CommandContext context, ArchiveSettings settings, CancellationToken cancellationToken)
    {
        using var contents = ArchiveFiles.Unlock(settings.Archive);
        foreach (var name in contents.Names) AnsiConsole.WriteLine(name);
        return 0;
    }
}
public sealed class ExtractCommand : Command<ExtractSettings>
{
    public override int Execute(CommandContext context, ExtractSettings settings, CancellationToken cancellationToken)
    {
        using var contents = ArchiveFiles.Unlock(settings.Archive);
        ArchiveFiles.ExtractNew(contents, settings.Output);
        AnsiConsole.WriteLine("Plaintext extracted. Protect this folder and remove it when finished.");
        return 0;
    }
}
public sealed class RekeyCommand : Command<RekeySettings>
{
    public override int Execute(CommandContext context, RekeySettings settings, CancellationToken cancellationToken)
    {
        ArchiveFiles.ValidateNewArchive(settings.Output);
        using var contents = ArchiveFiles.Unlock(settings.Archive);
        var password = PasswordPrompt.Read(confirm: true);
        try { ArchiveFiles.WriteNewArchive(settings.Output, SecretArchive.Encrypt(contents, password)); }
        finally { Array.Clear(password); }
        AnsiConsole.WriteLine("New archive and .sha256 checksum created. Deploy both files. The old archive still uses the old password.");
        return 0;
    }
}

public sealed class VerifyCommand : Command<ArchiveSettings>
{
    public override int Execute(CommandContext context, ArchiveSettings settings, CancellationToken cancellationToken)
    {
        ArchiveFiles.ReadVerified(settings.Archive, out var hasChecksum);
        AnsiConsole.WriteLine(hasChecksum
            ? "Archive length and SHA-256 checksum verified without a password. Use list to authenticate and inspect its contents."
            : "Legacy archive length and header passed; no .sha256 checksum found. Use list to authenticate it, then checksum to add integrity checking.");
        return 0;
    }
}

public sealed class ChecksumCommand : Command<ArchiveSettings>
{
    public override int Execute(CommandContext context, ArchiveSettings settings, CancellationToken cancellationToken)
    {
        var path = settings.Archive + ArchiveIntegrity.Suffix;
        ArchiveFiles.ValidateNewFile(path);
        var blob = ArchiveFiles.ReadVerified(settings.Archive, out _);
        var password = PasswordPrompt.Read();
        try
        {
            using var contents = SecretArchive.Decrypt(blob, password);
            ArchiveFiles.WriteNew(path, ArchiveIntegrity.CreateChecksum(blob));
        }
        finally { Array.Clear(password); }
        AnsiConsole.WriteLine("Archive authenticated and .sha256 checksum created. The encrypted archive was preserved. Deploy both files.");
        return 0;
    }
}

internal static class PasswordPrompt
{
    public static char[] Read(bool confirm = false)
    {
        if (Console.IsInputRedirected) throw new ToolOperationException("Run this command in an interactive console so it can prompt for a passphrase.");
        Console.WriteLine($"Passphrases must be {SecretArchive.MinimumPasswordLength} to 1024 characters. Input is hidden; Escape cancels.");
        var password = ReadOne(confirm ? "New archive passphrase: " : "Archive passphrase: ");
        try
        {
            if (password.Length < SecretArchive.MinimumPasswordLength || password.AsSpan().IsWhiteSpace())
                throw new ToolOperationException($"Passphrase must be at least {SecretArchive.MinimumPasswordLength} characters and cannot consist only of whitespace.");
            if (confirm)
            {
                var repeated = ReadOne("Confirm passphrase: ");
                try { if (!password.AsSpan().SequenceEqual(repeated)) throw new ToolOperationException("Passphrases did not match. Run the command again."); }
                finally { Array.Clear(repeated); }
            }
            return password;
        }
        catch { Array.Clear(password); throw; }
    }

    private static char[] ReadOne(string label)
    {
        Console.Write(label);
        var buffer = new char[1024];
        var length = 0;
        try
        {
            while (true)
            {
                var key = Console.ReadKey(true);
                if (key.Key == ConsoleKey.Escape) throw new OperationCanceledException();
                if (key.Key == ConsoleKey.Enter) { Console.WriteLine(); return buffer[..length]; }
                if (key.Key == ConsoleKey.Backspace) { if (length > 0) buffer[--length] = '\0'; }
                else if (!char.IsControl(key.KeyChar))
                {
                    if (length == buffer.Length) throw new ToolOperationException("Passphrase exceeds 1024 characters.");
                    buffer[length++] = key.KeyChar;
                }
            }
        }
        finally { Array.Clear(buffer); }
    }
}
