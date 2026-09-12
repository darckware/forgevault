namespace ForgeVault.Api.Authorization;

public sealed class MfaEnforcementOptions
{
    // Defaults to false until TOTP enrollment/verification exists (M6) — see
    // docs/ForgeVault.md §15 (MFA mandatory only for specific sensitive actions, not every
    // request) and docs/architecture/IMPLEMENTATION_READINESS.md §4.
    public bool Enabled { get; set; }
}
