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

// ===== YOUR STRING DATA - MODIFY THIS =====
var myWorkflowInput = "process-user-data-batch-123";
// Or from configuration: var myWorkflowInput = builder.Configuration["WorkflowInput"];
// Or from environment: var myWorkflowInput = Environment.GetEnvironmentVariable("WORKFLOW_INPUT") ?? "default-input";

// Pass the string to the auto-start service
builder.Services.AddSingleton(myWorkflowInput);
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
    public async Task<string> RunAsync(string workflowInput)
    {
        var logger = Workflow.Logger;
        logger.LogInformation("Starting recurring workflow with input: {Input}", workflowInput);

        try
        {
            // Pass the string directly to your activity
            var result = await Workflow.ExecuteActivityAsync(
                (RecurringActivities activities) => activities.DoRecurringTaskAsync(workflowInput),
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
    public async Task<string> DoRecurringTaskAsync(string workflowInput)
    {
        _logger.LogInformation("Executing recurring task with input: {Input} at {Timestamp}", 
            workflowInput, DateTime.UtcNow);

        // Use your string input in your business logic
        // For example, it could be:
        // - A file path to process
        // - A batch ID to work on
        // - A configuration key
        // - A queue name
        // - Any string data your activity needs

        // Simulate some work based on the input
        await Task.Delay(TimeSpan.FromSeconds(2));

        // Your actual business logic goes here
        // Use the workflowInput string to determine what to process
        var result = $"Processed '{workflowInput}' at {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}";
        
        _logger.LogInformation("Task completed: {Result}", result);
        return result;
    }
}

// ===== INPUT MODEL (SIMPLIFIED) =====

// No longer needed - we're just using a string directly!

// ===== AUTO-START SERVICE =====

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Temporalio.Client;
using Temporalio.Client.Schedules;
using Temporalio.Common;

public class WorkflowAutoStartService : IHostedService
{
    private readonly ITemporalClient _client;
    private readonly ILogger<WorkflowAutoStartService> _logger;
    private readonly string _workflowInput;
    private const string ScheduleId = "recurring-workflow-schedule";
    private const string TaskQueue = "recurring-task-queue";

    public WorkflowAutoStartService(
        ITemporalClient client, 
        ILogger<WorkflowAutoStartService> logger,
        string workflowInput)
    {
        _client = client;
        _logger = logger;
        _workflowInput = workflowInput;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Initializing auto-start service with input: {Input}", _workflowInput);

        // Wait a moment for worker to be ready
        await Task.Delay(2000, cancellationToken);

        try
        {
            await CreateOrUpdateScheduleAsync(cancellationToken);
            _logger.LogInformation("Schedule successfully created/updated with input: {Input}", _workflowInput);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create schedule with input: {Input}", _workflowInput);
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
        var schedule = new Schedule(
            Action: new ScheduleActionStartWorkflow(
                Workflow: "RecurringWorkflow",
                Args: new object[] { _workflowInput }, // Pass your string directly
                Options: new WorkflowOptions
                {
                    Id = $"recurring-workflow-{DateTime.UtcNow:yyyyMMdd}",
                    TaskQueue = TaskQueue,
                    RetryPolicy = new RetryPolicy
                    {
                        InitialInterval = TimeSpan.FromSeconds(1),
                        BackoffCoefficient = 2.0,
                        MaximumInterval = TimeSpan.FromMinutes(5),
                        MaximumAttempts = 3
                    }
                }),
            Spec: new ScheduleSpec
            {
                // Run every 30 seconds (adjust as needed)
                Intervals = new List<ScheduleIntervalSpec>
                {
                    new ScheduleIntervalSpec(Every: TimeSpan.FromSeconds(30))
                }
            })
        {
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
            _logger.LogInformation("Created new schedule: {ScheduleId}", ScheduleId);
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
  },
  "WorkflowSettings": {
    "DatabaseConnectionString": "Server=localhost;Database=MyApp;",
    "ApiKey": "your-api-key-here",
    "ProcessingBatchSize": 50,
    "EnableDetailedLogging": true,
    "ExternalServiceUrl": "https://external-service.com/api"
  }
}
*/

// ===== ALTERNATIVE: STRONGLY TYPED CONFIGURATION =====
/*
// Create a configuration class
public class WorkflowConfiguration
{
    public string Environment { get; set; } = "";
    public string Version { get; set; } = "1.0.0";
    public int MaxRetries { get; set; } = 3;
    public int TimeoutSeconds { get; set; } = 300;
    public List<string> Features { get; set; } = new();
    public Dictionary<string, object> CustomSettings { get; set; } = new();
}

// In Program.cs, use strongly typed configuration:
var workflowConfig = new WorkflowConfiguration
{
    Environment = builder.Environment.EnvironmentName,
    Version = "2.0.0",
    MaxRetries = 5,
    TimeoutSeconds = 600,
    Features = new List<string> { "feature1", "feature2" },
    CustomSettings = new Dictionary<string, object>
    {
        { "batchSize", 100 },
        { "apiEndpoint", "https://api.example.com" }
    }
};

// Or bind from configuration:
var workflowConfig = builder.Configuration.GetSection("WorkflowConfig").Get<WorkflowConfiguration>();

// Register as singleton
builder.Services.AddSingleton(workflowConfig);

// Then in WorkflowAutoStartService constructor:
public WorkflowAutoStartService(
    ITemporalClient client, 
    ILogger<WorkflowAutoStartService> logger,
    WorkflowConfiguration workflowConfig)
{
    _client = client;
    _logger = logger;
    _workflowConfig = workflowConfig;
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
