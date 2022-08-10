using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Nito.AsyncEx;
using NLog;

namespace AutopilotQuick.Steps;

public class RemoveDeviceFromAutopilotStep : StepBaseEx
{
    public readonly Logger Logger = LogManager.GetCurrentClassLogger();
    public override async Task<StepResult> Run(UserDataContext context, PauseToken pauseToken)
    {
        if (!IsEnabled)
        {
            Title = "Removing device from ap - DISABLED";
            await Task.Run(() => CountDown(pauseToken, 5000));
            return new StepResult(true, "Removing device from ap - DISABLED");
        }

        if (!context.TakeHomeToggleOn)
        {
            Progress = 100;
            return new StepResult(true, "Finished removing device from autopilot");
        }
        Title = "Removing device from autopilot";
        Progress = 0;
        Message = "Extracting files";
        IsIndeterminate = true;
        var scriptDir = Path.Combine(Path.GetDirectoryName(App.GetExecutablePath()), "Cache", "ScriptsTemp");
        Directory.CreateDirectory(scriptDir);
        var files = Assembly.GetExecutingAssembly().GetManifestResourceNames();
        
        var takehomeCredsCacher = new Cacher("http://nettools.psd202.org/AutoPilotFast/TakeHomeCreds.xml",
            "TakeHomeCreds.xml", context);


        if (!(takehomeCredsCacher.FileCached && takehomeCredsCacher.IsUpToDate))
        {
            takehomeCredsCacher.DownloadUpdate();
        }
        
        foreach (var fileName in files.Where(x => x.Contains("TakeHome")))
        {
            using (var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream(fileName))
            {
                using (var file = new FileStream(
                           Path.Combine(scriptDir, fileName.Replace("AutopilotQuick.Resources.TakeHome.", "")),
                           FileMode.Create, FileAccess.Write))
                {

                    resource.CopyTo(file);
                }
            }
        }

        Progress = 50;
        Message = "Running scripts...";
        var output = InvokePowershellScriptAndGetResult($@"
    function Get-Key{{
        $encoder = new-object System.Text.UTF8Encoding
        #Turns that key into a byte array so the securestring doesn't get angry
        return $encoder.Getbytes('{"SpPMOYjwbiruhLlZeWXmNyIsgvqxkRfT"}')
    }}
    function Decode-SecString(){{
        param
        (
        [Parameter(Mandatory=$true)]
        $secString
        )
        $decrypted = [System.Runtime.InteropServices.Marshal]::SecureStringToBSTR($secString)
        $decryptedPassword = [System.Runtime.InteropServices.Marshal]::PtrToStringAuto($decrypted)
        return $decryptedPassword
    }}
    $key = Get-Key
    $credStore = Import-Clixml -Path {takehomeCredsCacher.FilePath}
    $appid = Decode-SecString -secString (ConvertTo-SecureString -String $credStore.AppID -Key $key)
    $tenantid = Decode-SecString -secString (ConvertTo-SecureString -String $credStore.TenantID -Key $key)
    $clientSecret = Decode-SecString -secString (ConvertTo-SecureString -String $credStore.ClientSecret -Key $key)
    . {Path.TrimEndingDirectorySeparator(scriptDir)}\AutopilotCleanup.ps1
    Cleanup-Autopilot -appid $appid -tenantid $tenantid -clientsecret $clientSecret
    ");
        Progress = 100;
        Message = "Finished";
        Logger.Debug("Autopilot Script output: "+output);
        return new StepResult(true, "Finished removing device");
    }
}