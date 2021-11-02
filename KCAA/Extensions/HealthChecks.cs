using System.Linq;
using System.Net.Mime;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

namespace KCAA.Extensions
{
    public static class HealthChecks
    {
        public static IEndpointConventionBuilder MapCustomHealthChecks(this IEndpointRouteBuilder endpoints)
        {
            return endpoints.MapHealthChecks("/health", new HealthCheckOptions
            {
                ResponseWriter = async (context, report) =>
                {
                    var result = JsonSerializer.Serialize(
                        new
                        {
                            status = report.Status.ToString(),
                            checks = report.Entries.Select(e =>
                            new
                            {
                                check = e.Key,
                                status = e.Value.Status.ToString(),
                                data = e.Value.Data,
                                responseTimeMs = e.Value.Duration.TotalMilliseconds
                            }),
                            totalResponseTimeMs = report.TotalDuration.TotalMilliseconds
                        });
                    context.Response.ContentType = MediaTypeNames.Application.Json;
                    await context.Response.WriteAsync(result);
                }
            });
        }
    }
}
