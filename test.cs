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

// Add Temporal worker
builder.Services.AddHostedTemporalWorker(
    clientTargetHost: "localhost:7233",
    clientNamespace: "default",
    taskQueue: "my-task-queue")
    .AddWorkflow<MyWorkflow>()
    .AddScopedActivities<MyActivities>();

// ===== YOUR STRING INPUT =====
var myWorkflowInput = "process-startup-data";

// Add the auto-start service
builder.Services.AddSingleton(myWorkflowInput);
builder.Services.AddHostedService<WorkflowAutoStartService>();

var host = builder.Build();
await host.RunAsync();

// ===== WORKFLOW DEFINITION =====

using Temporalio.Workflows;

[Workflow]
public class MyWorkflow
{
    [WorkflowRun]
    public async Task<string> RunAsync(string input)
    {
        var logger = Workflow.Logger;
        logger.LogInformation("Starting workflow with input: {Input}", input);

        var result = await Workflow.ExecuteActivityAsync(
            (MyActivities activities) => activities.ProcessDataAsync(input),
            new ActivityOptions { StartToCloseTimeout = TimeSpan.FromMinutes(5) });

        logger.LogInformation("Workflow completed: {Result}", result);
        return result;
    }
}

// ===== ACTIVITY DEFINITION =====

public class MyActivities
{
    private readonly ILogger<MyActivities> _logger;

    public MyActivities(ILogger<MyActivities> logger)
    {
        _logger = logger;
    }

    [Activity]
    public async Task<string> ProcessDataAsync(string input)
    {
        _logger.LogInformation("Processing: {Input}", input);

        // Your business logic here
        await Task.Delay(TimeSpan.FromSeconds(2));

        var result = $"Processed '{input}' at {DateTime.UtcNow:HH:mm:ss}";
        _logger.LogInformation("Result: {Result}", result);
        
        return result;
    }
}

// ===== AUTO-START SERVICE =====

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Temporalio.Client;

public class WorkflowAutoStartService : IHostedService
{
    private readonly ITemporalClient _client;
    private readonly ILogger<WorkflowAutoStartService> _logger;
    private readonly string _input;

    public WorkflowAutoStartService(
        ITemporalClient client, 
        ILogger<WorkflowAutoStartService> logger,
        string input)
    {
        _client = client;
        _logger = logger;
        _input = input;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Auto-starting workflow with input: {Input}", _input);

        // Wait for worker to be ready
        await Task.Delay(1000, cancellationToken);

        try
        {
            var handle = await _client.StartWorkflowAsync(
                (MyWorkflow wf) => wf.RunAsync(_input),
                new WorkflowOptions
                {
                    Id = $"auto-workflow-{Guid.NewGuid()}",
                    TaskQueue = "my-task-queue"
                });

            _logger.LogInformation("Started workflow: {WorkflowId}", handle.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start workflow");
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Auto-start service stopped");
        return Task.CompletedTask;
    }
}

// ===== PACKAGE REFERENCES NEEDED =====
/*
<PackageReference Include="Temporalio" Version="1.6.0" />
<PackageReference Include="Temporalio.Extensions.Hosting" Version="1.6.0" />
<PackageReference Include="Microsoft.Extensions.Hosting" Version="8.0.0" />
<PackageReference Include="Microsoft.Extensions.Logging.Console" Version="8.0.0" />
*/
