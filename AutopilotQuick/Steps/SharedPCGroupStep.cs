using System.IO;
using System.Threading.Tasks;
using AQ.GroupManagementLibrary;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using Newtonsoft.Json;
using Nito.AsyncEx;

namespace AutopilotQuick.Steps;

public class SharedPCGroupStep : StepBaseEx
{
    public override string Name() => "Shared pc group step";

    public override async Task<StepResult> Run(UserDataContext context, PauseToken pauseToken,
        IOperationHolder<RequestTelemetry> StepOperation)
    {
        if (!IsEnabled)
        {
            Title = "SharedPC Step - Disabled";
            await CountDown(pauseToken, 5000);
            return new StepResult(true, "SharedPC Step - Disabled");
        }
        if (!context.UserRequestedChangeSharedPC && context.SharedPCCheckboxEnabled)
        {
            Title = "Disabling SharedPC box";
            Message =
                "Disabling sharedPC configuration options\nif you would like to change them, you need to do them now.";
            Progress = 0;
            await Task.Run(async () => await CountDown(pauseToken, 5000));
        }

        context.SharedPCCheckboxEnabled = false;
        if (!context.UserRequestedChangeSharedPC)
        {
            return new StepResult(true, "User did not request sharedPC change");
        }
        
        Title = "Applying SharedPC options";
        Message = "Waiting for internet";
        await InternetMan.WaitForInternetAsync(context);
        Message = "Getting client";
        IsIndeterminate = true;
        
        var groupManConfigCache = new Cacher(CachedResourceUris.GroupManConfig, context);
        if (!groupManConfigCache.IsUpToDate || !groupManConfigCache.FileCached)
        {
            await Task.Run(async ()=>await groupManConfigCache.DownloadUpdateAsync());
        }

        MainWindow.GroupManConfig config = JsonConvert.DeserializeObject<MainWindow.GroupManConfig>(await File.ReadAllTextAsync(groupManConfigCache.FilePath));
        var client = new GroupManagementClient(App.GetLogger<GroupManagementClient>(), config.APIKEY, config.URL);
        Message = "Figuring out if I am already a SharedPC...";
        var amIMember = await client.IsSharedPCMember(GetServiceTag(pauseToken));
        if (amIMember.HasValue)
        {
            if (amIMember.Value.TransitiveMemberInGroup)
            {
                Message = "I am a shared PC";
            }
            else
            {
                Message = "I am not a shared PC";
            }
            if (context.SharedPCChecked == amIMember.Value.TransitiveMemberInGroup)
            {
                return new StepResult(true, "SharedPC needed no changes");
            }
        }

        if (context.SharedPCChecked == true)
        {
            Message = "Adding to shared PC group";
            var result = await client.AddToSharedPCGroup(GetServiceTag(pauseToken));
            if (!result.HasValue)
            {
                Message = "Failed to add to shared PC group.";
                return new StepResult(false, "Failed to add to shared PC group.");
            }
            else
            {
                context.SharedPCChecked = result.Value.Success;
                return result.Value.Success ? new StepResult(true, "Added to shared PC group successfully") : new StepResult(false, "Failed to add to shared PC group.");
            }
        }
        else
        {
            Message = "Removing from SharedPC group";
            var result = await client.RemoveFromSharedPCGroup(GetServiceTag(pauseToken));
            if (!result.HasValue)
            {
                Message = "Failed to remove from shared PC group.";
                return new StepResult(false, "Failed to remove from shared PC group.");
            }
            else
            {
                context.SharedPCChecked = !result.Value.Success;
                return result.Value.Success ? new StepResult(true, "Removed from shared PC group successfully") : new StepResult(false, "Failed to remove from shared PC group.");
            }
        }
    }
}