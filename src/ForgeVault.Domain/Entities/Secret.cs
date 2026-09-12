namespace ForgeVault.Domain.Entities;

// docs/modules/03_SECRETS_AND_ENCRYPTION.md §4, taxonomy from docs/ForgeVault.md §78.
public enum SecretType
{
    Password,
    ApiKey,
    AccessToken,
    RefreshToken,
    LlmToken,
    SshPrivateKey,
    SshPassword,
    DatabaseCredential,
    OAuthClient,
    Certificate,
    PrivateKey,
    ServiceAccount,
    WebhookSecret,
    EnvSecret,
    TotpSeed,
    SystemCredential,
    GenericSecret,
}

// docs/modules/03_SECRETS_AND_ENCRYPTION.md §5 (states), docs/ForgeVault.md §102.
public enum SecretStatus
{
    Active,
    Suspended,
    Rotating,
    Expired,
    Revoked,
    Archived,
}

public sealed class Secret
{
    public Guid Id { get; set; }
    public Guid EnvironmentId { get; set; }
    public required string Name { get; set; }
    public SecretType Type { get; set; }
    public string? Provider { get; set; }
    public string? Description { get; set; }
    public Guid OwnerId { get; set; }
    public SecretStatus Status { get; set; } = SecretStatus.Active;
    public int CurrentVersion { get; set; } = 1;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public Guid? RotationPolicyId { get; set; }

    public Environment? Environment { get; set; }
    public ICollection<SecretVersion> Versions { get; set; } = new List<SecretVersion>();
}
