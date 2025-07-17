// Program.cs
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Temporalio.Client;
using Temporalio.Extensions.Hosting;
using Temporalio.Worker;

var builder = Host.CreateApplicationBuilder(args);

// Configure logging
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.SetMinimumLevel(LogLevel.Information);

// Add Temporal client
builder.Services.AddSingleton<ITemporalClient>(async provider =>
{
    var logger = provider.GetRequiredService<ILoggerFactory>();
    return await TemporalClient.ConnectAsync(new("localhost:7233")
    {
        LoggerFactory = logger,
        Namespace = "default"
    });
});

// Add Temporal worker with hosting extensions
builder.Services.AddHostedTemporalWorker(
    clientTargetHost: "localhost:7233",
    clientNamespace: "default",
    taskQueue: "recurring-task-queue")
    .AddWorkflow<RecurringWorkflow>()
    .AddScopedActivities<RecurringActivities>();

// Add the auto-start service
builder.Services.AddHostedService<WorkflowAutoStartService>();

var host = builder.Build();
await host.RunAsync();

// ===== WORKFLOW DEFINITION =====

using Temporalio.Workflows;

[Workflow]
public class RecurringWorkflow
{
    private static readonly ActivityOptions DefaultActivityOptions = new()
    {
        StartToCloseTimeout = TimeSpan.FromMinutes(5),
        RetryPolicy = new()
        {
            InitialInterval = TimeSpan.FromSeconds(1),
            BackoffCoefficient = 2.0,
            MaximumInterval = TimeSpan.FromMinutes(1),
            MaximumAttempts = 3
        }
    };

    [WorkflowRun]
    public async Task<string> RunAsync(RecurringWorkflowInput input)
    {
        var logger = Workflow.Logger;
        logger.LogInformation("Starting recurring workflow execution for: {TaskName}", input.TaskName);

        try
        {
            // Execute the main business logic
            var result = await Workflow.ExecuteActivityAsync(
                (RecurringActivities activities) => activities.DoRecurringTaskAsync(input),
                DefaultActivityOptions);

            logger.LogInformation("Recurring workflow completed successfully: {Result}", result);
            return result;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Recurring workflow failed");
            throw;
        }
    }
}

// ===== ACTIVITY DEFINITION =====

public class RecurringActivities
{
    private readonly ILogger<RecurringActivities> _logger;

    public RecurringActivities(ILogger<RecurringActivities> logger)
    {
        _logger = logger;
    }

    [Activity]
    public async Task<string> DoRecurringTaskAsync(RecurringWorkflowInput input)
    {
        _logger.LogInformation("Executing recurring task: {TaskName} at {Timestamp}", 
            input.TaskName, DateTime.UtcNow);

        // Simulate some work
        await Task.Delay(TimeSpan.FromSeconds(2));

        // Your actual business logic goes here
        // For example: data processing, cleanup, monitoring, etc.
        
        var result = $"Task '{input.TaskName}' completed at {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}";
        _logger.LogInformation("Task result: {Result}", result);
        
        return result;
    }
}

// ===== INPUT MODEL =====

public record RecurringWorkflowInput(
    string TaskName,
    Dictionary<string, object>? Parameters = null)
{
    public RecurringWorkflowInput() : this("DefaultTask") { } // Required for Temporal serialization
}

// ===== AUTO-START SERVICE =====

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Temporalio.Client;
using Temporalio.Client.Schedules;

public class WorkflowAutoStartService : IHostedService
{
    private readonly ITemporalClient _client;
    private readonly ILogger<WorkflowAutoStartService> _logger;
    private const string ScheduleId = "recurring-workflow-schedule";
    private const string TaskQueue = "recurring-task-queue";

    public WorkflowAutoStartService(
        ITemporalClient client, 
        ILogger<WorkflowAutoStartService> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Initializing auto-start service...");

        // Wait a moment for worker to be ready
        await Task.Delay(2000, cancellationToken);

        try
        {
            await CreateOrUpdateScheduleAsync(cancellationToken);
            _logger.LogInformation("Recurring workflow schedule successfully created/updated");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create recurring workflow schedule");
            throw; // Fail startup if we can't create the schedule
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping auto-start service...");
        
        try
        {
            // Optionally pause the schedule on shutdown
            var scheduleHandle = _client.GetScheduleHandle(ScheduleId);
            await scheduleHandle.PauseAsync("Application shutting down");
            _logger.LogInformation("Schedule paused for shutdown");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not pause schedule during shutdown");
        }
    }

    private async Task CreateOrUpdateScheduleAsync(CancellationToken cancellationToken)
    {
        var workflowInput = new RecurringWorkflowInput(
            TaskName: "AutoStarted-RecurringTask",
            Parameters: new Dictionary<string, object>
            {
                { "source", "auto-start" },
                { "environment", Environment.GetEnvironmentVariable("ENVIRONMENT") ?? "development" }
            });

        var schedule = new Schedule
        {
            Action = ScheduleActionStartWorkflow.Create(
                (RecurringWorkflow wf) => wf.RunAsync(workflowInput),
                new WorkflowOptions
                {
                    Id = $"recurring-workflow-{DateTime.UtcNow:yyyyMMdd}",
                    TaskQueue = TaskQueue,
                    RetryPolicy = new()
                    {
                        InitialInterval = TimeSpan.FromSeconds(1),
                        BackoffCoefficient = 2.0,
                        MaximumInterval = TimeSpan.FromMinutes(5),
                        MaximumAttempts = 3
                    }
                }),
            
            Spec = new ScheduleSpec
            {
                // Run every 30 seconds (adjust as needed)
                Intervals = new List<ScheduleIntervalSpec>
                {
                    new(Every: TimeSpan.FromSeconds(30))
                }
                
                // Alternative examples:
                // Every 5 minutes: new(Every: TimeSpan.FromMinutes(5))
                // Every hour: new(Every: TimeSpan.FromHours(1))
                
                // For calendar-based scheduling, use Calendars instead:
                // Calendars = new List<ScheduleCalendarSpec>
                // {
                //     new() { Hour = new[] { 9 }, Minute = new[] { 0 } } // Daily at 9:00 AM
                // }
            },
            
            Policy = new SchedulePolicy
            {
                // Skip if previous workflow is still running
                Overlap = ScheduleOverlapPolicy.Skip,
                
                // Allow catching up for missed runs within 1 hour
                CatchupWindow = TimeSpan.FromHours(1),
                
                // Pause schedule if workflows keep failing
                PauseOnFailure = true
            }
        };

        try
        {
            // Try to create the schedule
            await _client.CreateScheduleAsync(ScheduleId, schedule);
            _logger.LogInformation("Created new recurring workflow schedule: {ScheduleId}", ScheduleId);
        }
        catch (ScheduleAlreadyRunningException)
        {
            // Schedule already exists, update it
            _logger.LogInformation("Schedule already exists, updating: {ScheduleId}", ScheduleId);
            
            var handle = _client.GetScheduleHandle(ScheduleId);
            await handle.UpdateAsync(update =>
            {
                update.Schedule = schedule;
                return update;
            });
            
            _logger.LogInformation("Updated existing schedule: {ScheduleId}", ScheduleId);
        }
    }
}

// ===== CONFIGURATION (appsettings.json) =====
/*
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft": "Warning",
      "Microsoft.Hosting.Lifetime": "Information"
    }
  },
  "Temporal": {
    "ServerAddress": "localhost:7233",
    "Namespace": "default",
    "TaskQueue": "recurring-task-queue"
  }
}
*/

// ===== PACKAGE REFERENCES NEEDED =====
/*
<PackageReference Include="Temporalio" Version="1.6.0" />
<PackageReference Include="Temporalio.Extensions.Hosting" Version="1.6.0" />
<PackageReference Include="Microsoft.Extensions.Hosting" Version="8.0.0" />
<PackageReference Include="Microsoft.Extensions.Logging" Version="8.0.0" />
<PackageReference Include="Microsoft.Extensions.Logging.Console" Version="8.0.0" />
*/
