using System;
using System.Threading.Tasks;
using Azure.AI.Projects;
using Azure.Identity;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RepairPlanner.Services;
using RepairPlanner; // for RepairPlannerAgent

// Build the dependency injection container
var services = new ServiceCollection();

// logging
services.AddLogging(config => config.AddConsole());

// Cosmos DB configuration
var cosmosOptions = new CosmosDbOptions
{
    Endpoint = Environment.GetEnvironmentVariable("COSMOS_ENDPOINT") ?? string.Empty,
    Key = Environment.GetEnvironmentVariable("COSMOS_KEY") ?? string.Empty,
    DatabaseName = Environment.GetEnvironmentVariable("COSMOS_DATABASE_NAME") ?? string.Empty,
};
services.AddSingleton(cosmosOptions);

services.AddSingleton(_ => new CosmosClient(cosmosOptions.Endpoint, cosmosOptions.Key));
services.AddSingleton<CosmosDbService>();

// Fault mapping
services.AddSingleton<IFaultMappingService, FaultMappingService>();

// Azure AI / Foundry agent client
var aiEndpoint = Environment.GetEnvironmentVariable("AZURE_AI_PROJECT_ENDPOINT")
                 ?? throw new InvalidOperationException("AZURE_AI_PROJECT_ENDPOINT is not set");
var modelName = Environment.GetEnvironmentVariable("MODEL_DEPLOYMENT_NAME") ?? "gpt-4o";

services.AddSingleton(_ => new AIProjectClient(new Uri(aiEndpoint), new DefaultAzureCredential()));

services.AddSingleton(sp =>
{
    var client = sp.GetRequiredService<AIProjectClient>();
    var cosmos = sp.GetRequiredService<CosmosDbService>();
    var mapping = sp.GetRequiredService<IFaultMappingService>();
    var logger = sp.GetRequiredService<ILogger<RepairPlannerAgent>>();
    return new RepairPlannerAgent(client, cosmos, mapping, modelName, logger);
});

var provider = services.BuildServiceProvider();

// ensure agent version exists
var agent = provider.GetRequiredService<RepairPlannerAgent>();
await agent.EnsureAgentVersionAsync();

Console.WriteLine("RepairPlannerAgent is ready to plan work orders.");

// demonstrate workflow using a sample fault
var sampleFault = new RepairPlanner.Models.DiagnosedFault
{
    MachineId = "M-1001",
    FaultType = "curing_temperature_excessive",
    RootCause = "heater element failed",
    Severity = "high",
    DetectedAt = DateTimeOffset.UtcNow,
    Metadata = new Dictionary<string, object?> { ["shift"] = "night" }
};

try
{
    var workOrder = await agent.PlanAndCreateWorkOrderAsync(sampleFault);
    Console.WriteLine("Work order created:");
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(workOrder, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
}
catch (Exception ex)
{
    Console.WriteLine($"Error creating work order: {ex.Message}");
}

