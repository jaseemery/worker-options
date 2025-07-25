# Simple Pausable Temporal Workflow - Temporal UI Only

## Project Structure
```
TemporalPausableSample/
├── TemporalPausableSample.csproj
├── Program.cs
├── Models/
│   ├── ProcessingModels.cs
│   └── WorkflowState.cs
├── Workflows/
│   ├── PausableWorkflowBase.cs
│   ├── DataProcessingWorkflow.cs
│   └── OrderProcessingWorkflow.cs
├── Activities/
│   ├── IProcessingActivities.cs
│   └── ProcessingActivities.cs
└── Attributes/
    └── PausableWorkflowAttribute.cs
```

## 1. Project File (TemporalPausableSample.csproj)

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <OutputType>Exe</OutputType>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Temporalio" Version="1.1.0" />
    <PackageReference Include="Serilog.Extensions.Hosting" Version="8.0.0" />
    <PackageReference Include="Serilog.Sinks.Console" Version="5.0.0" />
    <PackageReference Include="Microsoft.Extensions.Hosting" Version="8.0.0" />
  </ItemGroup>

</Project>
```

## 2. Attributes/PausableWorkflowAttribute.cs

```csharp
using System.Reflection;

namespace TemporalPausableSample.Attributes;

/// <summary>
/// Simple attribute to mark workflows as pausable via Temporal UI signals
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public class PausableWorkflowAttribute : Attribute
{
    public string Description { get; set; } = "Pausable workflow";
}

public static class PausableWorkflowDiscovery
{
    public static IEnumerable<Type> FindPausableWorkflows(Assembly assembly)
    {
        return assembly.GetTypes()
            .Where(t => t.GetCustomAttribute<PausableWorkflowAttribute>() != null);
    }
    
    public static PausableWorkflowAttribute? GetConfiguration(Type workflowType)
    {
        return workflowType.GetCustomAttribute<PausableWorkflowAttribute>();
    }
}
```

## 3. Models/WorkflowState.cs

```csharp
namespace TemporalPausableSample.Models;

public class WorkflowState
{
    public bool IsPaused { get; set; }
    public DateTime? PausedAt { get; set; }
    public string Status { get; set; } = string.Empty;
    public int Progress { get; set; }
    public int Total { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = new();
}
```

## 4. Models/ProcessingModels.cs

```csharp
namespace TemporalPausableSample.Models;

public class DataRequest
{
    public List<DataItem> Items { get; set; } = new();
    public string RequestId { get; set; } = string.Empty;
}

public class DataItem
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public Dictionary<string, object> Properties { get; set; } = new();
}

public class ProcessingResult
{
    public bool Success { get; set; }
    public int ProcessedCount { get; set; }
    public int FailedCount { get; set; }
    public List<string> Errors { get; set; } = new();
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
}

public class OrderRequest
{
    public string OrderId { get; set; } = string.Empty;
    public List<OrderItem> Items { get; set; } = new();
    public CustomerInfo Customer { get; set; } = new();
    public PaymentInfo Payment { get; set; } = new();
}

public class OrderItem
{
    public string ProductId { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal Price { get; set; }
}

public class CustomerInfo
{
    public string CustomerId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
}

public class PaymentInfo
{
    public string PaymentMethod { get; set; } = string.Empty;
    public decimal Amount { get; set; }
}

public class OrderResult
{
    public bool Success { get; set; }
    public string OrderId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public List<string> ProcessedSteps { get; set; } = new();
}
```

## 5. Workflows/PausableWorkflowBase.cs

```csharp
using System.Reflection;
using Temporalio.Workflows;
using TemporalPausableSample.Attributes;
using TemporalPausableSample.Models;

namespace TemporalPausableSample.Workflows;

/// <summary>
/// Base class for pausable workflows - controlled via Temporal UI signals
/// </summary>
[Workflow]
public abstract class PausableWorkflowBase
{
    private bool _isPaused = false;
    private DateTime? _pausedAt;
    private PausableWorkflowAttribute? _config;
    
    protected PausableWorkflowAttribute Configuration
    {
        get
        {
            _config ??= GetType().GetCustomAttribute<PausableWorkflowAttribute>() 
                       ?? new PausableWorkflowAttribute();
            return _config;
        }
    }
    
    /// <summary>
    /// Pause signal - send this from Temporal UI
    /// Signal name: "pause"
    /// </summary>
    [WorkflowSignal("pause")]
    public virtual async Task Pause()
    {
        _isPaused = true;
        _pausedAt = Workflow.UtcNow;
        await OnPaused();
    }
    
    /// <summary>
    /// Resume signal - send this from Temporal UI  
    /// Signal name: "resume"
    /// </summary>
    [WorkflowSignal("resume")]
    public virtual async Task Resume()
    {
        _isPaused = false;
        _pausedAt = null;
        await OnResumed();
    }
    
    /// <summary>
    /// Query to check if paused - available in Temporal UI
    /// Query name: "isPaused"
    /// </summary>
    [WorkflowQuery("isPaused")]
    public virtual async Task<bool> IsPaused() => _isPaused;
    
    /// <summary>
    /// Query to get workflow state - available in Temporal UI
    /// Query name: "getWorkflowState"
    /// </summary>
    [WorkflowQuery("getWorkflowState")]
    public virtual async Task<WorkflowState> GetWorkflowState()
    {
        return new WorkflowState
        {
            IsPaused = _isPaused,
            PausedAt = _pausedAt,
            Status = GetCurrentStatus(),
            Progress = GetProgress(),
            Total = GetTotal(),
            Metadata = new Dictionary<string, object>
            {
                ["Description"] = Configuration.Description,
                ["WorkflowType"] = GetType().Name
            }
        };
    }
    
    /// <summary>
    /// Wait if the workflow is paused
    /// </summary>
    protected async Task WaitIfPaused()
    {
        if (_isPaused)
        {
            await Workflow.WaitConditionAsync(() => !_isPaused);
        }
    }
    
    /// <summary>
    /// Execute an activity and then check for pause (pause after activity completes)
    /// </summary>
    protected async Task<T> ExecuteActivityWithPause<T>(Func<Task<T>> activityCall, string activityName = "")
    {
        // Execute the activity first (no pause before)
        var result = await activityCall();
        
        // After activity completes, check if we should pause
        await WaitIfPaused();
        
        return result;
    }
    
    /// <summary>
    /// Execute an activity and then check for pause (pause after activity completes) - void return
    /// </summary>
    protected async Task ExecuteActivityWithPause(Func<Task> activityCall, string activityName = "")
    {
        // Execute the activity first (no pause before)
        await activityCall();
        
        // After activity completes, check if we should pause
        await WaitIfPaused();
    }
    
    // Abstract methods to implement
    protected abstract string GetCurrentStatus();
    protected abstract int GetProgress();
    protected abstract int GetTotal();
    
    // Virtual methods to override
    protected virtual async Task OnPaused() 
    {
        // Override for custom pause behavior
    }
    
    protected virtual async Task OnResumed() 
    {
        // Override for custom resume behavior
    }
}
```

## 6. Activities/IProcessingActivities.cs

```csharp
using Temporalio.Activities;
using TemporalPausableSample.Models;

namespace TemporalPausableSample.Activities;

public interface IProcessingActivities
{
    [Activity]
    Task<bool> ProcessDataItem(DataItem item);
    
    [Activity]
    Task<bool> ValidateDataItem(DataItem item);
    
    [Activity]
    Task<bool> SaveProcessingResult(string itemId, bool success);
    
    [Activity]
    Task<bool> ValidateOrder(OrderRequest order);
    
    [Activity]
    Task<bool> ReserveInventory(List<OrderItem> items);
    
    [Activity]
    Task<bool> ProcessPayment(PaymentInfo payment);
    
    [Activity]
    Task<bool> ShipOrder(string orderId);
    
    [Activity]
    Task<bool> SendConfirmationEmail(CustomerInfo customer, string orderId);
    
    [Activity]
    Task LogWorkflowEvent(string workflowId, string eventType, string message);
}
```

## 7. Activities/ProcessingActivities.cs

```csharp
using Temporalio.Activities;
using TemporalPausableSample.Models;

namespace TemporalPausableSample.Activities;

public class ProcessingActivities : IProcessingActivities
{
    [Activity]
    public async Task<bool> ProcessDataItem(DataItem item)
    {
        Console.WriteLine($"🔄 Processing data item: {item.Id}");
        
        // Simulate processing time
        await Task.Delay(TimeSpan.FromSeconds(3));
        
        // Simulate occasional failures
        var random = new Random();
        if (random.NextDouble() < 0.1) // 10% failure rate
        {
            Console.WriteLine($"❌ Failed to process item: {item.Id}");
            return false;
        }
        
        Console.WriteLine($"✅ Successfully processed item: {item.Id}");
        return true;
    }
    
    [Activity]
    public async Task<bool> ValidateDataItem(DataItem item)
    {
        Console.WriteLine($"🔍 Validating data item: {item.Id}");
        await Task.Delay(1000); // Simulate validation time
        
        // Simple validation - item must have name
        var isValid = !string.IsNullOrEmpty(item.Name);
        Console.WriteLine($"📋 Item {item.Id} validation result: {(isValid ? "✅ Valid" : "❌ Invalid")}");
        return isValid;
    }
    
    [Activity]
    public async Task<bool> SaveProcessingResult(string itemId, bool success)
    {
        Console.WriteLine($"💾 Saving processing result for {itemId}: {(success ? "✅ Success" : "❌ Failed")}");
        await Task.Delay(500); // Simulate database write
        return true;
    }
    
    [Activity]
    public async Task<bool> ValidateOrder(OrderRequest order)
    {
        Console.WriteLine($"🔍 Validating order: {order.OrderId}");
        await Task.Delay(1500);
        
        // Simple validation
        var isValid = order.Items.Any() && order.Payment.Amount > 0;
        Console.WriteLine($"📋 Order {order.OrderId} validation result: {(isValid ? "✅ Valid" : "❌ Invalid")}");
        return isValid;
    }
    
    [Activity]
    public async Task<bool> ReserveInventory(List<OrderItem> items)
    {
        Console.WriteLine($"📦 Reserving inventory for {items.Count} items");
        await Task.Delay(2500);
        
        // Simulate inventory check
        var random = new Random();
        var success = random.NextDouble() > 0.05; // 95% success rate
        
        Console.WriteLine($"📦 Inventory reservation result: {(success ? "✅ Reserved" : "❌ Failed")}");
        return success;
    }
    
    [Activity]
    public async Task<bool> ProcessPayment(PaymentInfo payment)
    {
        Console.WriteLine($"💳 Processing payment of ${payment.Amount} via {payment.PaymentMethod}");
        await Task.Delay(3500); // Simulate payment processing
        
        // Simulate payment processing
        var random = new Random();
        var success = random.NextDouble() > 0.02; // 98% success rate
        
        Console.WriteLine($"💳 Payment processing result: {(success ? "✅ Approved" : "❌ Declined")}");
        return success;
    }
    
    [Activity]
    public async Task<bool> ShipOrder(string orderId)
    {
        Console.WriteLine($"🚚 Shipping order: {orderId}");
        await Task.Delay(2000);
        
        Console.WriteLine($"🚚 Order {orderId} shipped successfully");
        return true;
    }
    
    [Activity]
    public async Task<bool> SendConfirmationEmail(CustomerInfo customer, string orderId)
    {
        Console.WriteLine($"📧 Sending confirmation email to {customer.Email} for order {orderId}");
        await Task.Delay(800);
        
        Console.WriteLine($"📧 Confirmation email sent successfully");
        return true;
    }
    
    [Activity]
    public async Task LogWorkflowEvent(string workflowId, string eventType, string message)
    {
        Console.WriteLine($"📝 Workflow {workflowId} - {eventType}: {message}");
        await Task.Delay(100);
    }
}
```

## 8. Workflows/DataProcessingWorkflow.cs

```csharp
using Temporalio.Workflows;
using TemporalPausableSample.Attributes;
using TemporalPausableSample.Models;
using TemporalPausableSample.Activities;

namespace TemporalPausableSample.Workflows;

[PausableWorkflow(Description = "Data processing workflow that can be paused via Temporal UI")]
[Workflow("DataProcessingWorkflow")]
public class DataProcessingWorkflow : PausableWorkflowBase
{
    private string _currentStatus = "Starting";
    private int _processedItems = 0;
    private int _totalItems = 0;
    private List<string> _errors = new();
    private DateTime _startTime;
    
    [WorkflowRun]
    public async Task<ProcessingResult> ProcessData(DataRequest request)
    {
        _startTime = Workflow.UtcNow;
        _totalItems = request.Items.Count;
        _currentStatus = "Initializing";
        
        Console.WriteLine($"🚀 Starting data processing workflow for {_totalItems} items");
        
        var activities = Workflow.CreateActivityStub<IProcessingActivities>(
            new ActivityOptions
            {
                StartToCloseTimeout = TimeSpan.FromMinutes(5),
                RetryPolicy = new RetryPolicy
                {
                    MaximumAttempts = 3,
                    InitialInterval = TimeSpan.FromSeconds(1),
                    MaximumInterval = TimeSpan.FromSeconds(10)
                }
            });
        
        // Log start - with pause handling
        await ExecuteActivityWithPause(
            () => activities.LogWorkflowEvent(Workflow.Info.WorkflowId, "Started", 
                $"Processing {_totalItems} items"));
        
        _currentStatus = "Processing Items";
        
        foreach (var item in request.Items)
        {
            _currentStatus = $"Processing item {item.Id}";
            Console.WriteLine($"📋 Current status: {_currentStatus}");
            
            try
            {
                // Validate item - pauses after if pause signal was sent
                var isValid = await ExecuteActivityWithPause(
                    () => activities.ValidateDataItem(item));
                
                if (!isValid)
                {
                    _errors.Add($"Validation failed for item {item.Id}");
                    continue;
                }
                
                // Process the item - pauses after if pause signal was sent
                var success = await ExecuteActivityWithPause(
                    () => activities.ProcessDataItem(item));
                
                // Save result - pauses after if pause signal was sent
                await ExecuteActivityWithPause(
                    () => activities.SaveProcessingResult(item.Id, success));
                
                if (success)
                {
                    _processedItems++;
                }
                else
                {
                    _errors.Add($"Processing failed for item {item.Id}");
                }
            }
            catch (Exception ex)
            {
                _errors.Add($"Exception processing item {item.Id}: {ex.Message}");
                Console.WriteLine($"❌ Error processing {item.Id}: {ex.Message}");
            }
        }
        
        _currentStatus = "Finalizing";
        
        // Log completion - with pause handling
        await ExecuteActivityWithPause(
            () => activities.LogWorkflowEvent(Workflow.Info.WorkflowId, "Completed", 
                $"Processed {_processedItems}/{_totalItems} items"));
        
        _currentStatus = "Completed";
        Console.WriteLine($"✅ Data processing completed: {_processedItems}/{_totalItems} items processed");
        
        return new ProcessingResult
        {
            Success = _errors.Count == 0,
            ProcessedCount = _processedItems,
            FailedCount = _totalItems - _processedItems,
            Errors = _errors,
            StartTime = _startTime,
            EndTime = Workflow.UtcNow
        };
    }
    
    protected override string GetCurrentStatus() => _currentStatus;
    protected override int GetProgress() => _processedItems;
    protected override int GetTotal() => _totalItems;
    
    protected override async Task OnPaused()
    {
        var oldStatus = _currentStatus;
        _currentStatus = $"⏸️ PAUSED - {_currentStatus}";
        Console.WriteLine($"⏸️ Workflow paused at: {oldStatus}");
    }
    
    protected override async Task OnResumed()
    {
        _currentStatus = _currentStatus.Replace("⏸️ PAUSED - ", "");
        Console.WriteLine($"▶️ Workflow resumed at: {_currentStatus}");
    }
    
    [WorkflowQuery("getErrors")]
    public async Task<List<string>> GetErrors() => _errors;
    
    [WorkflowQuery("getElapsedTime")]  
    public async Task<TimeSpan> GetElapsedTime() => Workflow.UtcNow - _startTime;
}
```

## 9. Workflows/OrderProcessingWorkflow.cs

```csharp
using Temporalio.Workflows;
using TemporalPausableSample.Attributes;
using TemporalPausableSample.Models;
using TemporalPausableSample.Activities;

namespace TemporalPausableSample.Workflows;

[PausableWorkflow(Description = "Order processing workflow that can be paused via Temporal UI")]
[Workflow("OrderProcessingWorkflow")]
public class OrderProcessingWorkflow : PausableWorkflowBase
{
    private string _currentStatus = "Starting";
    private int _currentStep = 0;
    private int _totalSteps = 5;
    private List<string> _completedSteps = new();
    
    [WorkflowRun]
    public async Task<OrderResult> ProcessOrder(OrderRequest request)
    {
        Console.WriteLine($"🚀 Starting order processing workflow for order: {request.OrderId}");
        
        var activities = Workflow.CreateActivityStub<IProcessingActivities>(
            new ActivityOptions
            {
                StartToCloseTimeout = TimeSpan.FromMinutes(10),
                RetryPolicy = new RetryPolicy
                {
                    MaximumAttempts = 3,
                    InitialInterval = TimeSpan.FromSeconds(2)
                }
            });
        
        await ExecuteActivityWithPause(
            () => activities.LogWorkflowEvent(Workflow.Info.WorkflowId, "Started", 
                $"Processing order {request.OrderId}"));
        
        try
        {
            // Step 1: Validate Order
            await ExecuteStep("Validating Order", async () =>
            {
                var isValid = await ExecuteActivityWithPause(
                    () => activities.ValidateOrder(request));
                
                if (!isValid)
                    throw new InvalidOperationException("Order validation failed");
                return "Order validated successfully";
            });
            
            // Step 2: Reserve Inventory  
            await ExecuteStep("Reserving Inventory", async () =>
            {
                var reserved = await ExecuteActivityWithPause(
                    () => activities.ReserveInventory(request.Items));
                
                if (!reserved)
                    throw new InvalidOperationException("Inventory reservation failed");
                return "Inventory reserved successfully";
            });
            
            // Step 3: Process Payment
            await ExecuteStep("Processing Payment", async () =>
            {
                var paymentSuccess = await ExecuteActivityWithPause(
                    () => activities.ProcessPayment(request.Payment));
                
                if (!paymentSuccess)
                    throw new InvalidOperationException("Payment processing failed");
                return "Payment processed successfully";
            });
            
            // Step 4: Ship Order
            await ExecuteStep("Shipping Order", async () =>
            {
                var shipped = await ExecuteActivityWithPause(
                    () => activities.ShipOrder(request.OrderId));
                
                if (!shipped)
                    throw new InvalidOperationException("Order shipping failed");
                return "Order shipped successfully";
            });
            
            // Step 5: Send Confirmation
            await ExecuteStep("Sending Confirmation", async () =>
            {
                var emailSent = await ExecuteActivityWithPause(
                    () => activities.SendConfirmationEmail(request.Customer, request.OrderId));
                
                if (!emailSent)
                    throw new InvalidOperationException("Confirmation email failed");
                return "Confirmation email sent successfully";
            });
            
            _currentStatus = "Completed";
            
            await ExecuteActivityWithPause(
                () => activities.LogWorkflowEvent(Workflow.Info.WorkflowId, "Completed", 
                    $"Order {request.OrderId} processed successfully"));
            
            Console.WriteLine($"✅ Order processing completed successfully: {request.OrderId}");
            
            return new OrderResult
            {
                Success = true,
                OrderId = request.OrderId,
                Status = "Completed",
                ProcessedSteps = _completedSteps
            };
        }
        catch (Exception ex)
        {
            _currentStatus = $"Failed: {ex.Message}";
            Console.WriteLine($"❌ Order processing failed: {ex.Message}");
            
            await ExecuteActivityWithPause(
                () => activities.LogWorkflowEvent(Workflow.Info.WorkflowId, "Failed", ex.Message));
            
            return new OrderResult
            {
                Success = false,
                OrderId = request.OrderId,
                Status = _currentStatus,
                ProcessedSteps = _completedSteps
            };
        }
    }
    
    private async Task ExecuteStep(string stepName, Func<Task<string>> stepAction)
    {
        _currentStatus = stepName;
        _currentStep++;
        Console.WriteLine($"📋 Step {_currentStep}/{_totalSteps}: {stepName}");
        
        var result = await stepAction();
        _completedSteps.Add($"Step {_currentStep}: {result}");
        Console.WriteLine($"✅ {result}");
    }
    
    protected override string GetCurrentStatus() => _currentStatus;
    protected override int GetProgress() => _currentStep;
    protected override int GetTotal() => _totalSteps;
    
    protected override async Task OnPaused()
    {
        var oldStatus = _currentStatus;
        _currentStatus = $"⏸️ PAUSED - {_currentStatus}";
        Console.WriteLine($"⏸️ Order workflow paused at step {_currentStep}/{_totalSteps}: {oldStatus}");
    }
    
    protected override async Task OnResumed()
    {
        _currentStatus = _currentStatus.Replace("⏸️ PAUSED - ", "");
        Console.WriteLine($"▶️ Order workflow resumed at step {_currentStep}/{_totalSteps}: {_currentStatus}");
    }
    
    [WorkflowQuery("getCompletedSteps")]
    public async Task<List<string>> GetCompletedSteps() => _completedSteps;
}
```

## 10. Program.cs

```csharp
using Serilog;
using Temporalio.Client;
using Temporalio.Extensions.Hosting;
using TemporalPausableSample.Activities;
using TemporalPausableSample.Attributes;
using TemporalPausableSample.Workflows;
using TemporalPausableSample.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateLogger();

var builder = Host.CreateApplicationBuilder(args);

// Add Serilog
builder.Services.AddSerilog();

// Add Temporal client
builder.Services.AddSingleton<ITemporalClient>(provider =>
{
    return TemporalClient.ConnectAsync(new TemporalClientConnectOptions
    {
        TargetHost = "localhost:7233", // Default Temporal server
        Namespace = "default"
    }).Result;
});

// Add activities
builder.Services.AddSingleton<ProcessingActivities>();

// Add Temporal worker
builder.Services.AddHostedTemporalWorker(
    "pausable-queue",
    workerOptions =>
    {
        workerOptions.AddWorkflow<DataProcessingWorkflow>();
        workerOptions.AddWorkflow<OrderProcessingWorkflow>();
        workerOptions.AddAllActivities(typeof(ProcessingActivities).Assembly);
    });

// Add a service to start some sample workflows
builder.Services.AddHostedService<WorkflowStarterService>();

var app = builder.Build();

// Log discovered pausable workflows
var pausableWorkflows = PausableWorkflowDiscovery.FindPausableWorkflows(typeof(Program).Assembly);
foreach (var workflowType in pausableWorkflows)
{
    var config = PausableWorkflowDiscovery.GetConfiguration(workflowType);
    Log.Information("📋 Discovered pausable workflow: {WorkflowType} - {Description}",
        workflowType.Name, config?.Description);
}

Log.Information("🚀 Simple Pausable Workflow Sample started");
Log.Information("🌐 Temporal UI available at: http://localhost:8080");
Log.Information("📝 Use Temporal UI to send 'pause' and 'resume' signals to workflows");

await app.RunAsync();

// Service to start sample workflows automatically
public class WorkflowStarterService : IHostedService
{
    private readonly ITemporalClient _client;
    private Timer? _timer;
    
    public WorkflowStarterService(ITemporalClient client)
    {
        _client = client;
    }
    
    public Task StartAsync(CancellationToken cancellationToken)
    {
        Console.WriteLine("🕐 Starting sample workflows in 5 seconds...");
        
        // Start workflows after a delay to let the worker initialize
        _timer = new Timer(StartSampleWorkflows, null, TimeSpan.FromSeconds(5), TimeSpan.FromMinutes(2));
        
        return Task.CompletedTask;
    }
    
    private async void StartSampleWorkflows(object? state)
    {
        try
        {
            // Start a data processing workflow
            await StartDataProcessingWorkflow();
            
            // Start an order processing workflow
            await StartOrderProcessingWorkflow();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Error starting workflows: {ex.Message}");
        }
    }
    
    private async Task StartDataProcessingWorkflow()
    {
        var workflowId = $"data-processing-{DateTime.Now:yyyyMMdd-HHmmss}";
        
        var request = new DataRequest
        {
            RequestId = Guid.NewGuid().ToString(),
            Items = Enumerable.Range(1, 8).Select(i => new DataItem
            {
                Id = $"item-{i:D3}",
                Name = $"Sample Item {i}",
                Properties = new Dictionary<string, object>
                {
                    ["Type"] = "Sample",
                    ["Priority"] = i % 3 + 1
                }
            }).ToList()
        };
        
        var options = new WorkflowOptions
        {
            Id = workflowId,
            TaskQueue = "pausable-queue"
        };
        
        var handle = await _client.StartWorkflowAsync<DataProcessingWorkflow>(
            wf => wf.ProcessData(request), options);
        
        Console.WriteLine($"📊 Started Data Processing Workflow: {workflowId}");
        Console.WriteLine($"   ➡️ Temporal UI: http://localhost:8080/namespaces/default/workflows/{workflowId}");
    }
    
    private async Task StartOrderProcessingWorkflow()
    {
        var orderId = $"ORDER-{DateTime.Now:yyyyMMdd}-{Random.Shared.Next(1000, 9999)}";
        var workflowId = $"order-processing-{orderId}";
        
        var request = new OrderRequest
        {
            OrderId = orderId,
            Items = new List<OrderItem>
            {
                new() { ProductId = "PROD-001", Quantity = 2, Price = 29.99m },
                new() { ProductId = "PROD-002", Quantity = 1, Price = 49.99m }
            },
            Customer = new CustomerInfo
            {
                CustomerId = "CUST-12345",
                Name = "John Doe",
                Email = "john.doe@example.com"
            },
            Payment = new PaymentInfo
            {
                PaymentMethod = "CreditCard",
                Amount = 109.97m
            }
        };
        
        var options = new WorkflowOptions
        {
            Id = workflowId,
            TaskQueue = "pausable-queue"
        };
        
        var handle = await _client.StartWorkflowAsync<OrderProcessingWorkflow>(
            wf => wf.ProcessOrder(request), options);
        
        Console.WriteLine($"🛒 Started Order Processing Workflow: {workflowId}");
        Console.WriteLine($"   ➡️ Temporal UI: http://localhost:8080/namespaces/default/workflows/{workflowId}");
    }
    
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _timer?.Dispose();
        return Task.CompletedTask;
    }
}
```

## Usage Instructions

### 1. Setup Temporal Server
```bash
# Using Docker
docker run --rm -p 7233:7233 -p 8080:8080 temporalio/auto-setup:latest

# Or using Temporal CLI
temporal server start-dev
```

### 2. Run the Application
```bash
dotnet run
```

### 3. Use Temporal UI to Control Workflows

The application will automatically start sample workflows every 2 minutes. To control them:

#### Open Temporal UI:
Navigate to `http://localhost:8080`

#### Find Your Workflows:
- Look for workflows starting with `data-processing-` or `order-processing-`
- Click on a workflow to see its details

#### Send Signals via Temporal UI:
1. **To Pause a Workflow:**
   - In the workflow details page, find the "Signal" section
   - Signal Name: `pause`
   - Signal Input: `{}` (empty JSON object)
   - Click "Send Signal"

2. **To Resume a Workflow:**
   - Signal Name: `resume`  
   - Signal Input: `{}`
   - Click "Send Signal"

#### Query Workflow State:
1. **Check if Paused:**
   - Query Name: `isPaused`
   - No input required

2. **Get Full State:**
   - Query Name: `getWorkflowState`
   - Returns current status, progress, and metadata

3. **Get Errors (Data Processing):**
   - Query Name: `getErrors`
   - Returns list of processing errors

## Key Features

✅ **Pure Temporal UI Control** - No REST APIs, all control via Temporal UI  
✅ **Automatic Workflow Generation** - Sample workflows start automatically  
✅ **Visual Console Output** - Rich console logging with emojis  
✅ **Multiple Workflow Types** - Data processing and order processing examples  
✅ **Clean Pause Points** - Always pauses between activities, never during  
✅ **Query Support** - Check workflow state and progress via Temporal UI  

## How to Test

1. **Run the application** - Sample workflows will start automatically
2. **Open Temporal UI** at `http://localhost:8080`
3. **Find a running workflow** and click on it
4. **Send a "pause" signal** - Watch the console output show the pause
5. **Send a "resume" signal** - Watch the workflow continue
6. **Use queries** to check workflow state anytime

The workflows will show clear console output indicating when they pause and resume, making it easy to see the pause functionality in action!
