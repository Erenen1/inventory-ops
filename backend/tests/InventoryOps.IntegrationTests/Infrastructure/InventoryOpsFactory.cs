using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace InventoryOps.IntegrationTests.Infrastructure;

public sealed class InventoryOpsFactory(IReadOnlyDictionary<string, string?> settings) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        // UseSetting values are visible to Program while services are being registered.
        foreach (var (key, value) in settings)
        {
            builder.UseSetting(key, value);
        }
    }
}
