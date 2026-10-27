using System.Diagnostics;
using System.IO;

namespace AutopilotQuick.Steps;

internal static class DellBiosCommand
{
    internal static ProcessStartInfo ClearSetupPassword(string workingDirectory, string password)
    {
        var startInfo = new ProcessStartInfo(Path.Combine(workingDirectory, "cctk.exe"))
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        // PowerShell 5.1 can strip embedded quotes and change non-ASCII stdin.
        // Pass native arguments directly; never put the password in a script or log.
        startInfo.ArgumentList.Add("--setuppwd=");
        startInfo.ArgumentList.Add("--valsetuppwd=" + password.TrimEnd('\r', '\n'));
        return startInfo;
    }
}
