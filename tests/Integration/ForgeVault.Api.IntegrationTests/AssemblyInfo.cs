using System.Runtime.Versioning;

// Boots the real Api host (WebApplicationFactory<Program>) for SecretsCrudFlowTests,
// which is Linux-only — see ForgeVault.Api/AssemblyInfo.cs.
[assembly: SupportedOSPlatform("linux")]
