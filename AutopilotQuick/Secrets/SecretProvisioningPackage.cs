using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using AutopilotQuick.Steps;

namespace AutopilotQuick;

internal static class SecretProvisioningPackage
{
    public static async Task<string> ApplyAsync(StepBaseEx step, string name)
    {
        if (!App.RunningOnWinPE() || !Directory.Exists(@"X:\"))
            throw new InvalidOperationException("Secret provisioning packages require WinPE.");
        var folder = Path.Combine(@"X:\AQ\SecretRuntime", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(folder, name);
        var bytes = AppSecrets.ReadBytes(name);
        try
        {
            Directory.CreateDirectory(folder);
            await File.WriteAllBytesAsync(path, bytes);
            return await step.InvokePowershellScriptAndGetResultAsync(
                "DISM /Image=W:\\ /Add-ProvisioningPackage /PackagePath:'" + path.Replace("'", "''") + "'", CancellationToken.None);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
        }
    }
}
