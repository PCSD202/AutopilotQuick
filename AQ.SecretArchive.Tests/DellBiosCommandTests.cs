using System.Diagnostics;
using System.Text;
using AutopilotQuick.Steps;
using NUnit.Framework;

namespace AQ.SecretArchive.Tests;

[TestFixture, Platform("Win")]
public class DellBiosCommandTests
{
    private string _directory = null!;

    [OneTimeSetUp]
    public async Task BuildFakeNativeTool()
    {
        _directory = Path.Combine(Path.GetTempPath(), "aq-fake-bios-" + Guid.NewGuid().ToString("N") + " with spaces");
        Directory.CreateDirectory(_directory);
        // This executable only echoes synthetic arguments and exits with a known code.
        // No Dell binary, BIOS command, app startup, or hardware access is used by these tests.
        const string compilerScript = @"
$ErrorActionPreference = 'Stop'
Add-Type -OutputAssembly $env:AQ_FAKE_CCTK_EXE -OutputType ConsoleApplication -TypeDefinition @'
using System;
using System.Text;
public class FakeCctk {
    public static int Main(string[] args) {
        Console.WriteLine(Convert.ToBase64String(Encoding.UTF8.GetBytes(string.Join(""\n"", args))));
        return 42;
    }
}
'@
";
        var startInfo = new ProcessStartInfo(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe"))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-Command", "-" })
            startInfo.ArgumentList.Add(argument);
        startInfo.Environment["AQ_FAKE_CCTK_EXE"] = Path.Combine(_directory, "cctk.exe");
        using var process = Process.Start(startInfo)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        await process.StandardInput.WriteLineAsync(compilerScript);
        process.StandardInput.Close();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try { await process.WaitForExitAsync(timeout.Token); }
        finally
        {
            if (!process.HasExited) process.Kill(true);
            await process.WaitForExitAsync();
        }
        await output;
        Assert.That(process.ExitCode, Is.Zero, await errors);
        Assert.That(File.Exists(Path.Combine(_directory, "cctk.exe")), Is.True);
    }

    [TestCase("synthetic-simple", "synthetic-simple")]
    [TestCase("synthetic space", "synthetic space")]
    [TestCase("synthetic\"quote", "synthetic\"quote")]
    [TestCase("syntheticλ", "syntheticλ")]
    [TestCase("synthetic'quote", "synthetic'quote")]
    [TestCase("synthetic&|!%$", "synthetic&|!%$")]
    [TestCase("synthetic\\\"quote\\", "synthetic\\\"quote\\")]
    [TestCase(" synthetic \r\n", " synthetic ")]
    public async Task NativeToolReceivesExactPasswordAndSingleSpaceReplacement(string input, string expected)
    {
        using var process = Process.Start(DellBiosCommand.ClearSetupPassword(_directory, input))!;
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try { await process.WaitForExitAsync(timeout.Token); }
        finally
        {
            if (!process.HasExited) process.Kill(true);
            await process.WaitForExitAsync();
        }
        Assert.That(await errors, Is.Empty);
        var actual = Encoding.UTF8.GetString(Convert.FromBase64String((await output).Trim()));
        Assert.That(actual, Is.EqualTo("--setuppwd= \n--valsetuppwd=" + expected),
            "Dell requires a single blank space, not a zero-length replacement password.");
        Assert.That(process.ExitCode, Is.EqualTo(42), "The fake tool's nonzero exit code must be preserved.");
    }

    [OneTimeTearDown]
    public void RemoveSyntheticTool()
    {
        if (_directory is not null && Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }
}
