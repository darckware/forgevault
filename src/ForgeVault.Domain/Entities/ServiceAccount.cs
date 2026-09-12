namespace ForgeVault.Domain.Entities;

// docs/ForgeVault.md §31-32, §76; docs/modules/02_IDENTITY_AND_AUTHENTICATION.md §4 (UC-04).
// M7: the first non-human identity type — ForgeHub/ForgeRouter/other services authenticate
// as one of these, never reusing a human User's credentials for automated integration
// (docs/ForgeVault.md §31 "Nunca reutilizar usuário humano para integrações").
public sealed class ServiceAccount
{
    public Guid Id { get; set; }

    // e.g. "forgehub", "forgerouter" — the identity string is conventionally "service:<name>".
    public required string Name { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }

    public ICollection<ServiceAccountToken> Tokens { get; set; } = new List<ServiceAccountToken>();
}
