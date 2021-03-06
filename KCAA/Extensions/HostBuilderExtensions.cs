using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;

namespace KCAA.Extensions
{
    public static class HostBuilderExtensions
    {
        public static IHostBuilder UseConfiguredSerilog(this IHostBuilder hostBuilder)
        {
            hostBuilder.UseSerilog((_, _, configuration) => configuration
                .WriteTo.Console()
                //.WriteTo.AzureBlobStorage(connectionString, LogEventLevel.Warning, )
                );

            return hostBuilder;
        }

    }
}
