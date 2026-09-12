using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ForgeVault.Infrastructure.Persistence;

// Used only by `dotnet ef migrations add/remove` at design time, so that migrations can be
// authored against this project without needing to spin up the full Api host. Runtime
// registration (the real connection string, from configuration) lives in ForgeVault.Api's
// Program.cs — see docs/architecture/IMPLEMENTATION_READINESS.md, milestone M1.
public sealed class ForgeVaultDbContextFactory : IDesignTimeDbContextFactory<ForgeVaultDbContext>
{
    public ForgeVaultDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            System.Environment.GetEnvironmentVariable("FORGEVAULT_CONNECTION")
            ?? "Host=localhost;Port=5435;Database=forgevault;Username=forgevault;Password=forgevault_dev_only";

        var optionsBuilder = new DbContextOptionsBuilder<ForgeVaultDbContext>()
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention();

        return new ForgeVaultDbContext(optionsBuilder.Options);
    }
}
