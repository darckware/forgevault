using ForgeVault.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ForgeVault.Api.IntegrationTests;

// docs/architecture/IMPLEMENTATION_READINESS.md, milestone M1:
// "one Integration test asserts the DbContext can round-trip an Organization row."
public sealed class OrganizationRoundTripTests : IClassFixture<ForgeVaultDbContextFixture>
{
    private readonly ForgeVaultDbContextFixture _fixture;

    public OrganizationRoundTripTests(ForgeVaultDbContextFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task CreateAndReloadOrganization_RoundTripsAllFields()
    {
        var db = _fixture.Db;
        var now = DateTimeOffset.UtcNow;

        var organization = new Organization
        {
            Id = Guid.NewGuid(),
            Name = $"acme-{Guid.NewGuid():N}",
            Slug = $"acme-{Guid.NewGuid():N}",
            Status = OrganizationStatus.Active,
            CreatedAt = now,
            UpdatedAt = now,
        };

        db.Organizations.Add(organization);
        await db.SaveChangesAsync();

        // Force a real round-trip through Postgres, not just the change tracker's cache.
        db.ChangeTracker.Clear();

        var reloaded = await db.Organizations.SingleAsync(o => o.Id == organization.Id);

        Assert.Equal(organization.Name, reloaded.Name);
        Assert.Equal(organization.Slug, reloaded.Slug);
        Assert.Equal(OrganizationStatus.Active, reloaded.Status);
    }
}
