using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Helpers;
using Colors.Net;
using Fclp;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Extensions.Logging;

namespace Azure.Functions.Cli.Actions.KeysActions
{
    [Action(Name = "get", Context = Context.Keys, HelpText = "Get API keys")]
    class GetKeyAction : BaseAction
    {
        public bool IsMasterKey { get; set; }
        public bool IsFunctionsKey { get; set; }
        public string FunctionName { get; set; }
        public string KeyName { get; set; } = "default";
        public string Value { get; set; }
        public bool ValueStdin { get; set; }
        public override ICommandLineParserResult ParseArgs(string[] args)
        {
            SetFlag<bool>("master", "create or update the master key", m => IsMasterKey = m);
            SetFlag<bool>("functions", "create or update a functions key", f => IsFunctionsKey = f);
            SetFlag<string>("function", "create or update a key for a function", f => FunctionName = f);
            SetFlag<string>("name", "key name", n => KeyName = n);
            SetFlag<string>("value", "key value", v => Value = v);
            SetFlag<bool>("value-stdin", "key value from stdin", v => ValueStdin = v);
            return base.ParseArgs(args);
        }
    }
}