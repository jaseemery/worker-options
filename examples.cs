using Temporalio.Activities;
using Temporalio.Workflows;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Temporalio.Client;
using Temporalio.Worker;

namespace Tempolate
{
    // Data model for complex processing
    public class ProcessRequest
    {
        public string Data { get; set; } = string.Empty;
        public Dictionary<string, object>? Options { get; set; }
        public DateTime? ProcessingDate { get; set; }
    }

    public class ProcessResult
    {
        public string Result { get; set; } = string.Empty;
        public bool Success { get; set; }
        public DateTime ProcessedAt { get; set; }
    }

    public class MyActivities
    {
        private readonly ILogger<MyActivities> _logger;

        public MyActivities(ILogger<MyActivities> logger)
        {
            _logger = logger;
        }

        // Overload 1: Process simple string
        [Activity("stringinput")]
        public async Task<string> Process(string input)
        {
            _logger.LogInformation("Processing simple string: {Input}", input);
            await Task.Delay(100);
            return $"Processed string: {input.ToUpper()}";
        }

        // Overload 2: Process with count
        [Activity("strinandint")]
        public async Task<string> Process(string input, int count)
        {
            _logger.LogInformation("Processing string with count: {Input}, {Count}", input, count);
            await Task.Delay(100);
            return string.Concat(Enumerable.Repeat($"{input.ToUpper()}", count));
        }

        // Overload 3: Process complex request
        [Activity("complexobject")]
        public async Task<ProcessResult> Process(ProcessRequest request)
        {
            _logger.LogInformation("Processing complex request for data: {Data}", request.Data);
            await Task.Delay(100);

            return new ProcessResult
            {
                Result = $"Processed complex data: {request.Data.ToUpper()}",
                Success = true,
                ProcessedAt = request.ProcessingDate ?? DateTime.UtcNow
            };
        }

        // Overload 4: Process multiple inputs
        [Activity]
        public async Task<List<string>> Process(List<string> inputs)
        {
            _logger.LogInformation("Processing multiple inputs: {Count} items", inputs.Count);
            await Task.Delay(100);

            return inputs.Select(input => $"Processed: {input.ToUpper()}").ToList();
        }
    }

    // Example workflow that uses the overloaded activities
    [Workflow]
    public interface IProcessingWorkflow
    {
        [WorkflowRun]
        Task<List<string>> RunProcessingWorkflow(string baseData);
    }

    [Workflow]
    public class ProcessingWorkflow : IProcessingWorkflow
    {
        [WorkflowRun]
        public async Task<List<string>> RunProcessingWorkflow(string baseData)
        {

            var results = new List<string>();

            try
            {
                // Call the simple overload
                var simpleResult = await activities.Process(baseData);
                results.Add($"Simple: {simpleResult}");

                // Call the count overload
                var countResult = await activities.Process(baseData, 3);
                results.Add($"Count: {countResult}");

                // Call the complex overload
                var complexRequest = new ProcessRequest
                {
                    Data = baseData,
                    Options = new Dictionary<string, object>
                    {
                        ["priority"] = "high",
                        ["retries"] = 3,
                        ["timeout"] = 30
                    },
                    ProcessingDate = DateTime.UtcNow
                };

                var complexResult = await activities.Process(complexRequest);
                results.Add($"Complex: {complexResult.Result}");

                // Call the multiple inputs overload
                var multipleInputs = new List<string> { "input1", "input2", "input3" };
                var multipleResults = await activities.Process(multipleInputs);

                return results;
            }
            catch (Exception ex)
            {
                // Log the error and add to results
                results.Add($"Error: {ex.Message}");
                return results;
            }
        }
    }

}
