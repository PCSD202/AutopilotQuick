#region

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using Nito.AsyncEx;
using NLog;

#endregion

namespace AutopilotQuick.Steps;

public class BatchStep : StepBaseEx
{
    public override string Name() => "Batch step";
    public readonly Logger Logger = LogManager.GetCurrentClassLogger();
    private List<StepBaseEx> Steps = new List<StepBaseEx>();
    public BatchStep(List<StepBaseEx> StepsToRun)
    {
        Steps = StepsToRun;
        foreach (var step in Steps)
        {
            //Hook the update event for each step
            step.StepUpdated += StepOnStepUpdated;
        }
    }
    
    string ComputeSingleStepMessage(StepBase step)
    {
        StringBuilder builder = new StringBuilder();
        builder.Append(step.Title);
        if (step.Message != "")
        {
            builder.Append(" - ");
            builder.Append(step.Message);
        }
        if (!step.IsIndeterminate)
        {
            builder.Append(" - ");
            builder.Append($"{step.Progress / 100:P0}");
        }

        return builder.ToString();
    }

    string ComputeMessageBlock()
    {
        var output = new StringBuilder();
        foreach (var thing in StepTaskMapping)
        {
            if (!thing.Value.IsCompleted)
            {
                output.AppendLine(ComputeSingleStepMessage(thing.Key));
            }
            
        }

        return output.ToString();
    }
    
    private List<Task<StepResult>> StepTasks = new();

    private Dictionary<StepBase, Task<StepResult>> StepTaskMapping = new Dictionary<StepBase, Task<StepResult>>();
    public override async Task<StepResult> Run(UserDataContext context, PauseToken pauseToken,
        IOperationHolder<RequestTelemetry> StepOperation)
    {
        foreach (var step in Steps)
        {
            //Add the step run function to the StepTasks list
            var tas = step.Run(context, pauseToken, StepOperation);
            
            StepTasks.Add(tas);
            StepTaskMapping.Add(step, tas);
        }

        StepOnStepUpdated(this, new StepStatus());
        try
        {
            await Task.WhenAll(StepTasks);
        }
        catch (Exception e)
        {
            Logger.Error(e);
        }
        
        return new StepResult(true, $"Completed {Steps.Count} tasks.");
    }
    
    private void StepOnStepUpdated(object? sender, StepStatus e)
    {
        Title = $"Running tasks. {StepTasks.Count(x => x.IsCompleted)} of {StepTasks.Count} complete";
        Message = ComputeMessageBlock();
        Progress = Steps.Average(x => x.Progress);
    }
}