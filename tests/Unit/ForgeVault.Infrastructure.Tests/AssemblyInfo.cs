using System.Runtime.Versioning;

// This test project directly exercises Unix file-mode permission checks
// (LocalFileKeyProvider) and only makes sense on Linux — same rationale as the
// [SupportedOSPlatform("linux")] annotation on LocalFileKeyProvider itself.
[assembly: SupportedOSPlatform("linux")]
