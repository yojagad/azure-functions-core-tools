using System;
using System.Linq;
using System.Threading.Tasks;
using Colors.Net;
using Newtonsoft.Json;
using Azure.Functions.Cli.Interfaces;
using DurableTask.Core;
using DurableTask.AzureStorage;
using DurableTask.Core.History;
using Newtonsoft.Json.Linq;

using static Colors.Net.StringStaticMethods;

namespace Azure.Functions.Cli.Common
{
    internal class DurableManager : IDurableManager
    {
        private readonly ISecretsManager _secretsManager;

        private readonly AzureStorageOrchestrationService _orchestrationService;

        private readonly TaskHubClient _client;

        public DurableManager(ISecretsManager secretsManager)
        {
            _secretsManager = secretsManager;

            var connectionString = secretsManager.GetSecrets().FirstOrDefault(s => s.Key.Equals("AzureWebJobsStorage", StringComparison.OrdinalIgnoreCase)).Value;
            if (connectionString == null)
            {
                throw new CliException("Unable to access connection string.");
            }

            var settings = new AzureStorageOrchestrationServiceSettings
            {
                TaskHubName = "DurableFunctionsHub",
                StorageConnectionString = connectionString,
            };

            _orchestrationService = new AzureStorageOrchestrationService(settings);
            _client = new TaskHubClient(_orchestrationService);
        }

        public async Task StartNew(string functionName, string version, string instanceId, object input)
        {
            if (string.IsNullOrEmpty(functionName))
            {
                throw new CliArgumentsException("Must specify the name of of the orchestration function to start.",
                    new CliArgument { Name = "functionName", Description = "Name of the orchestration function to start." });
            }

            await _client.CreateOrchestrationInstanceAsync(functionName, version, instanceId, input);

            var status = await _client.GetOrchestrationStateAsync(instanceId, false);

            if (status != null)
            {
                ColoredConsole.WriteLine(Yellow($"Started {status[0].Name} with new instance {status[0].OrchestrationInstance.InstanceId} at {status[0].CreatedTime}."));
            }
            else
            {
                throw new CliException($"Could not start new instance {instanceId}.");
            }
        }

        public async Task Terminate(string instanceId, string reason)
        {
            if (string.IsNullOrEmpty(instanceId))
            {
                throw new CliArgumentsException("Must specify the id of the orchestration instance you wish to terminate.",
                    new CliArgument { Name = "id", Description = "ID of the orchestration instance to terminate." });
            }

            var orchestrationInstance = new OrchestrationInstance
            {
                InstanceId = instanceId
            };

            await _client.TerminateInstanceAsync(orchestrationInstance, reason);

            // TODO - why are we checking the state
            var status = await _client.GetOrchestrationStateAsync(instanceId, false);

            // TODO - provide better information (success/failure)
            ColoredConsole.WriteLine(Yellow($"Termination message sent to instance {instanceId}"));
        }

        public async Task Rewind(string instanceId, string reason)
        {
            if (string.IsNullOrEmpty(instanceId))
            {
                throw new CliArgumentsException("Must specify the id of the orchestration instance you wish to rewind.",
                    new CliArgument { Name = "id", Description = "ID of the orchestration instance to rewind." });
            }

            await _orchestrationService.RewindTaskOrchestrationAsync(instanceId, reason);
            var status = await _client.GetOrchestrationStateAsync(instanceId, false);

            ColoredConsole.WriteLine(Yellow($"Rewind message sent to instance {instanceId}"));
        }

        public async Task RaiseEvent(string instanceId, string eventName, object eventData)
        {
            if (string.IsNullOrEmpty(instanceId))
            {
                throw new CliArgumentsException("Must specify the id of the orchestration instance you wish to raise an event for.",
                    new CliArgument { Name = "id", Description = "ID of the orchestration instance to raise an event for." });
            }

            var orchestrationInstance = new OrchestrationInstance
            {
                InstanceId = instanceId
            };

            await _client.RaiseEventAsync(orchestrationInstance, eventName, eventData);

            ColoredConsole.WriteLine(Yellow($"Raised event {eventName} to instance {instanceId} with data {eventData}"));
        }

        public async Task GetHistory(string instanceId)
        {
            if (string.IsNullOrEmpty(instanceId))
            {
                throw new CliArgumentsException("Must specify the id of the orchestration instance you wish to retrieve the history for.",
                    new CliArgument { Name = "id", Description = "ID of the orchestration instance to retrieve the history of." });
            }

            var historyString = await _orchestrationService.GetOrchestrationHistoryAsync(instanceId, null);

            JArray history = JArray.Parse(historyString);

            JArray chronological_history = new JArray(history.OrderBy(obj => (string)obj["TimeStamp"]));
            foreach (JObject jobj in chronological_history)
            {
                var parsed = Enum.TryParse(jobj["EventType"].ToString(), out EventType eventName);
                jobj["EventType"] = eventName.ToString();
            }

            // TODO - what actually prints here
            ColoredConsole.Write(Yellow($"History: {chronological_history.ToString(Formatting.Indented)}"));
        }

        public async Task GetRuntimeStatus(string instanceId, bool allExecutions)
        {
            if (string.IsNullOrEmpty(instanceId))
            {
                throw new CliArgumentsException("Must specify the id of the orchestration instance you wish to get the runtime status of.",
                    new CliArgument { Name = "id", Description = "ID of the orchestration instance for which to retrieve the runtime status." });
            }

            var statuses = await _client.GetOrchestrationStateAsync(instanceId, allExecutions);

            foreach (OrchestrationState status in statuses)
            {
                ColoredConsole.WriteLine(Yellow($"Name: {status.Name}"))
                    .WriteLine(Yellow($"Instance: {status.OrchestrationInstance}"))
                    .WriteLine(Yellow($"Version: {status.Version}"))
                    .WriteLine(Yellow($"TimeCreated: {status.CreatedTime}"))
                    .WriteLine(Yellow($"CompletedTime: {status.CompletedTime}"))
                    .WriteLine(Yellow($"LastUpdatedTime: {status.LastUpdatedTime}"))
                    .WriteLine(Yellow($"Input: {status.Input}"))
                    .WriteLine(Yellow($"Output: {status.Output}"))
                    .WriteLine(Yellow($"Status: {status.OrchestrationStatus}"))
                    .WriteLine();
            }
        }

        public async Task DeleteHistory()
        {
            await _orchestrationService.DeleteAsync();
            ColoredConsole.Write(Green("History and instance store successfully deleted."));
        }
    }
}