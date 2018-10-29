using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Text;

namespace Azure.Functions.Cli.Interfaces
{
    public interface ITokenBuilder : IConfigureBuilder<IWebJobsBuilder>
    {

    }
}
