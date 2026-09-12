using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging;

namespace ForgeVault.Security.Tests;

// Boots the real Api host (same as ForgeVault.E2E.Tests) but additionally captures every
// formatted log message the app writes during a request, so tests can assert on what
// actually got logged.
public sealed class LoggingWebApplicationFactory : WebApplicationFactory<Program>
{
    public List<string> CapturedLogs { get; } = [];

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureLogging(logging => logging.AddProvider(new CapturingLoggerProvider(CapturedLogs)));
    }
}
