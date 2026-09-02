using System.Linq;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Neura.Infrastructure.Persistence;

namespace Neura.E2E.Tests;

/// <summary>
/// Unlike NeuraWebApplicationFactory in Neura.Tests (which talks to the
/// app through an in-memory HttpMessageHandler), a real browser needs a
/// real TCP socket to navigate to. This factory boots the actual Kestrel
/// server on a random localhost port so Playwright can drive it exactly
/// like a person would in a browser, while still swapping Postgres for
/// EF Core's InMemory provider so the test doesn't need a database.
/// </summary>
public class PlaywrightWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = $"neura-e2e-tests-{Guid.NewGuid():N}";

    public string ServerAddress { get; private set; } = default!;

    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.ConfigureWebHost(webHost =>
        {
            webHost.UseEnvironment("Testing");
            // WebApplicationFactory defaults to TestServer, which has no real TCP
            // listener. Playwright requires a real browser-reachable socket, so
            // explicitly host the application with Kestrel on an ephemeral port.
            webHost.UseKestrel();
            webHost.UseUrls("http://127.0.0.1:0");
            webHost.ConfigureServices(services =>
            {
                var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<NeuraDbContext>));
                if (descriptor is not null) services.Remove(descriptor);
                services.AddDbContext<NeuraDbContext>(options => options.UseInMemoryDatabase(_databaseName));
            });
        });

        var host = builder.Build();
        host.Start();

        var server = host.Services.GetRequiredService<IServer>();
        var addresses = server.Features.Get<IServerAddressesFeature>();
        ServerAddress = addresses!.Addresses.First();

        return host;
    }
}
