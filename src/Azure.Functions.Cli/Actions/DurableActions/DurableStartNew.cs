using System;
using System.Threading.Tasks;
using Azure.Functions.Cli.Interfaces;
using Fclp;

namespace Azure.Functions.Cli.Actions.DurableActions
{
    [Action(Name = "start-new", Context = Context.Durable, HelpText = "Starts a new instance of a specified orchestratior function")]
    class DurableStartNew : BaseAction
    {
        public string FunctionName { get; set; }

        public string InstanceID { get; set; }

        public object Input { get; set; }

        public string Version { get; set; }

        private readonly IDurableManager _durableManager;

        public DurableStartNew(IDurableManager durableManager)
        {
            _durableManager = durableManager;
        }

        public override ICommandLineParserResult ParseArgs(string[] args)
        {
            Parser
                 .Setup<string>("id")
                 .WithDescription("This is the id for a new instance")
                 .SetDefault($"{Guid.NewGuid():N}")
                 .Callback(i => InstanceID = i);
            Parser
                .Setup<string>("functionName")
                .WithDescription("This is the name of the orchestrator function for the new instance")
                .SetDefault(null)
                .Callback(n => FunctionName = n);
            Parser
               .Setup<string>("input")
               .WithDescription("This is the orchestrator function's input object")
               .SetDefault(null)
               .Callback(p => Input = p);
            Parser
               .Setup<string>("version")
               .WithDescription("This shows up in the help next to the version option.")
               .SetDefault(null)
               .Callback(v => Version = v);

            return base.ParseArgs(args);
        }

        public override async Task RunAsync()
        {
            await _durableManager.StartNew(FunctionName, Version, InstanceID, Input);
        }
    }
}