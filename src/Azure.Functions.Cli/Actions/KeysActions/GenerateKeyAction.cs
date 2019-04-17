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
    [Action(Name = "generate", Context = Context.Keys, HelpText = "Generate API keys")]
    class GenerateKeyAction : BaseAction
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

        public override async Task RunAsync()
        {
            ValidateFlags();
            var repo = new FileSystemSecretsRepository(Path.Combine(ScriptHostHelpers.GetFunctionAppRootDirectory(Environment.CurrentDirectory), ".keys"));
            var manager = new SecretManager(repo, new FakeLogger(), new FakeMetricsLogger(), createHostSecretsIfMissing: true);
            var value = GetKeyValue();
            if (IsMasterKey)
            {
                var key = await manager.SetMasterKeyAsync(value);
                ColoredConsole.WriteLine(key.Secret);
            }
            else if (IsFunctionsKey)
            {
                var key = await manager.AddOrUpdateFunctionSecretAsync(KeyName, value, "functionkeys", ScriptSecretsType.Host);
                ColoredConsole.WriteLine(key.Secret);
            }
            else if (!string.IsNullOrEmpty(FunctionName))
            {
                var key = await manager.AddOrUpdateFunctionSecretAsync(KeyName, Value, FunctionName, ScriptSecretsType.Function);
                ColoredConsole.WriteLine(key.Secret);
            }
        }

        private string GetKeyValue()
        {
            if (!string.IsNullOrWhiteSpace(Value))
            {
                return Value;
            }
            else if (ValueStdin)
            {
                return Console.ReadLine();
            }
            return null;
        }

        private void ValidateFlags()
        {
            var isFunction = !string.IsNullOrEmpty(FunctionName);
            if (!IsMasterKey && !IsFunctionsKey && !isFunction)
            {
                throw new CliArgumentsException("either --master, --functions or --function <name> is reqired.");
            }

            if ((IsMasterKey && (IsFunctionsKey || isFunction)) ||
                (IsFunctionsKey && (IsMasterKey || isFunction)) ||
                (isFunction && (IsMasterKey || IsFunctionsKey)))
            {
                throw new CliArgumentsException("only one of --master, --functions and --function <name> is expected");
            }
        }
    }

    class FakeLogger : ILogger
    {
        public IDisposable BeginScope<TState>(TState state)
        {
            return new FakeIDisposable();
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return false;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        { }
    }

    internal class FakeIDisposable : IDisposable
    {
        public void Dispose()
        {
        }
    }

    class FakeMetricsLogger : IMetricsLogger
    {
        public object BeginEvent(string eventName, string functionName = null, string data = null)
        {
            return null;
        }

        public void BeginEvent(MetricEvent metricEvent)
        {
        }

        public void EndEvent(MetricEvent metricEvent)
        {
        }

        public void EndEvent(object eventHandle)
        {
        }

        public void LogEvent(MetricEvent metricEvent)
        {
        }

        public void LogEvent(string eventName, string functionName = null, string data = null)
        {
        }
    }
}