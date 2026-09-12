using System.Runtime.Versioning;

// Boots the real Api host (WebApplicationFactory<Program>), which is Linux-only —
// see ForgeVault.Api/AssemblyInfo.cs.
[assembly: SupportedOSPlatform("linux")]
