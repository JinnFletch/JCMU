using JinnDev.JCMU.AddonManager.Models;

namespace JinnDev.JCMU.CoreTools.Tools;

internal record PublishContext
{
    public required string TargetDirectory { get; init; }
    public string ProjectFilePath { get; init; } = string.Empty;
    public string ManifestFilePath { get; init; } = string.Empty;
    public PluginManifest? Manifest { get; init; }
    public string InitialVersion { get; init; } = string.Empty;

    // Stubs for Step 2 properties
    public string Owner { get; init; } = string.Empty;
    public string Repo { get; init; } = string.Empty;
    public string FinalVersion { get; init; } = string.Empty;
}
