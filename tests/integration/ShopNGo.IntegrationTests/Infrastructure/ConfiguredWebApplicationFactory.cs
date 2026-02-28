using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace ShopNGo.IntegrationTests.Infrastructure;

internal sealed class ConfiguredWebApplicationFactory<TMarker>(IReadOnlyDictionary<string, string?> settings)
    : WebApplicationFactory<TMarker>
    where TMarker : class
{
    private readonly IReadOnlyDictionary<string, string?> _settings = settings;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(_settings);
        });
    }
}
