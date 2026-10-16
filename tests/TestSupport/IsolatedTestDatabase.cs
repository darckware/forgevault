using ForgeVault.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ForgeVault.TestSupport;

// Shared by every database-backed test project. Refuse a non-test database before
// opening a connection, so a changed appsettings file cannot point tests at production.
public static class IsolatedTestDatabase
{
    private static readonly object SetupLock = new();

    public static string Prepare(string defaultDatabase)
    {
        var connectionString = Environment.GetEnvironmentVariable("FORGEVAULT_TEST_CONNECTION")
            ?? $"Host=localhost;Port=5435;Database={defaultDatabase};Username=forgevault;Password=forgevault_dev_only";
        Validate(connectionString);

        lock (SetupLock)
        {
            EnsureCreatedAndMigrated(connectionString);
        }
        return connectionString;
    }

    private static void EnsureCreatedAndMigrated(string connectionString)
    {
        var settings = new NpgsqlConnectionStringBuilder(connectionString);
        var databaseName = settings.Database!;
        settings.Database = "postgres";
        using (var connection = new NpgsqlConnection(settings.ConnectionString))
        {
            connection.Open();
            using var exists = new NpgsqlCommand("SELECT 1 FROM pg_database WHERE datname = @name", connection);
            exists.Parameters.AddWithValue("name", databaseName);
            if (exists.ExecuteScalar() is null)
            {
                using var create = new NpgsqlCommand($"CREATE DATABASE \"{databaseName.Replace("\"", "\"\"")}\"", connection);
                create.ExecuteNonQuery();
            }
        }

        var options = new DbContextOptionsBuilder<ForgeVaultDbContext>()
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention()
            .Options;
        using var db = new ForgeVaultDbContext(options);
        db.Database.Migrate();
    }

    public static void Validate(string connectionString)
    {
        var database = new NpgsqlConnectionStringBuilder(connectionString).Database;
        if (string.IsNullOrWhiteSpace(database) || !database.EndsWith("_test", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Tests require a database whose name ends in _test.");
        }
    }
}

public sealed class IsolatedWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var database = typeof(IsolatedWebApplicationFactory).Assembly.GetName().Name?.Contains("E2E", StringComparison.Ordinal) == true
            ? "forgevault_e2e_test"
            : "forgevault_integration_test";
        builder.UseSetting("ConnectionStrings:ForgeVault", IsolatedTestDatabase.Prepare(database));
    }
}
