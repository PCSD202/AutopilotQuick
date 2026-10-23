using Spectre.Console;
using Spectre.Console.Cli;

namespace AQ.SecretArchive.Tool;

public static class Program
{
    public static int Main(string[] args)
    {
        var app = new CommandApp();
        app.Configure(config =>
        {
            config.SetApplicationName("AQ.SecretArchive.Tool");
            config.AddCommand<CreateCommand>("create").WithDescription("Encrypt a folder into a new archive.");
            config.AddCommand<CheckCommand>("check").WithDescription("Check source filenames and size limits without reading file contents.");
            config.AddCommand<ListCommand>("list").WithDescription("Unlock and list filenames without showing values.");
            config.AddCommand<ExtractCommand>("extract").WithDescription("Unlock into a NEW private plaintext folder.");
            config.AddCommand<RekeyCommand>("rekey").WithDescription("Write a new archive with a new password.");
            config.SetExceptionHandler((error, _) =>
            {
                // Exception details can contain secret JSON, paths, or values. Never echo them.
                AnsiConsole.WriteLine(ToolErrors.Describe(error));
                return 1;
            });
        });
        return app.Run(args);
    }
}
