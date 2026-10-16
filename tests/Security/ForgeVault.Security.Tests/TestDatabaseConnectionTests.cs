namespace ForgeVault.Security.Tests;

public sealed class TestDatabaseConnectionTests
{
    [Theory]
    [InlineData("Host=localhost;Database=forgevault;Username=test;Password=test")]
    [InlineData("Host=localhost;Database=postgres;Username=test;Password=test")]
    public void Factory_rejects_non_test_database(string connectionString)
    {
        Assert.Throws<InvalidOperationException>(() => LoggingWebApplicationFactory.ValidateTestConnection(connectionString));
    }

    [Fact]
    public void Factory_accepts_test_database()
    {
        LoggingWebApplicationFactory.ValidateTestConnection(
            "Host=localhost;Database=forgevault_test;Username=test;Password=test");
    }
}
