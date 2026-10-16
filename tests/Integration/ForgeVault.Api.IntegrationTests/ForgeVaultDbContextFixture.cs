using ForgeVault.Infrastructure.Persistence;
using ForgeVault.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace ForgeVault.Api.IntegrationTests;

// Uses a dedicated database on the Postgres instance started by docker compose
// (host port 5435). FORGEVAULT_TEST_CONNECTION may override only with a *_test database.
public sealed class ForgeVaultDbContextFixture : IDisposable
{
    public ForgeVaultDbContext Db { get; }

    public ForgeVaultDbContextFixture()
    {
        var connectionString = IsolatedTestDatabase.Prepare("forgevault_integration_test");

        var options = new DbContextOptionsBuilder<ForgeVaultDbContext>()
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention()
            .Options;

        Db = new ForgeVaultDbContext(options);
    }

    public void Dispose() => Db.Dispose();
}
