using ForgeVault.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ForgeVault.Api.IntegrationTests;

// Points at the same Postgres instance started by `docker compose up -d postgres`
// (docker-compose.yml, host port 5435) — see docs/architecture/IMPLEMENTATION_READINESS.md
// milestone M1. Override FORGEVAULT_TEST_CONNECTION to point elsewhere (e.g. CI).
public sealed class ForgeVaultDbContextFixture : IDisposable
{
    public ForgeVaultDbContext Db { get; }

    public ForgeVaultDbContextFixture()
    {
        var connectionString =
            System.Environment.GetEnvironmentVariable("FORGEVAULT_TEST_CONNECTION")
            ?? "Host=localhost;Port=5435;Database=forgevault;Username=forgevault;Password=forgevault_dev_only";

        var options = new DbContextOptionsBuilder<ForgeVaultDbContext>()
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention()
            .Options;

        Db = new ForgeVaultDbContext(options);
    }

    public void Dispose() => Db.Dispose();
}
