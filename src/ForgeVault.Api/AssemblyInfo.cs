using System.Runtime.Versioning;

// ForgeVault.Api resolves LocalFileKeyProvider at startup, which itself is Linux-only
// (Unix file-mode permission checks have no Windows equivalent) — consistent with
// docs/ForgeVault.md §13's on-host Linux deployment model for the MVP.
[assembly: SupportedOSPlatform("linux")]
