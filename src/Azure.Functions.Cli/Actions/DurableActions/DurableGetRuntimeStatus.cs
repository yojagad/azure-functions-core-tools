using System;
using System.Threading.Tasks;
using Azure.Functions.Cli.Interfaces;
using Fclp;

namespace Azure.Functions.Cli.Actions.DurableActions
{
    [Action(Name = "get-runtime-status", Context = Context.Durable, HelpText = "Retrieve the status of the specified orchestration instance")]
    class DurableGetRuntimeStatus : BaseDurableAction
    {
        public bool AllExecutions { get; set; }

        private readonly IDurableManager _durableManager;

        public DurableGetRuntimeStatus(IDurableManager durableManager)
        {
            _durableManager = durableManager;
        }

        public override ICommandLineParserResult ParseArgs(string[] args)
        {
            Parser
                 .Setup<bool>("all-executions")
                 .WithDescription("This specifies the name of an event to raise")
                 .SetDefault(false)
                 .Callback(e => AllExecutions = e);
            return base.ParseArgs(args);
        }

        public override async Task RunAsync()
        {
            await _durableManager.GetRuntimeStatus(Id, AllExecutions);
        }
    }
}