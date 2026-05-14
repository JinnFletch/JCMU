namespace JinnDev.JCMU.AddonManager.Models;

/// <summary>
/// Represents a specific version (Git Tag/Release) of an addon.
/// </summary>
public record AddonVersionInfo
{
    /// <summary>
    /// The semantic version string (e.g., "v1.0.4").
    /// </summary>
    public required string VersionName { get; init; }

    /// <summary>
    /// The specific Git commit hash tied to this version, ensuring immutability.
    /// </summary>
    public required string CommitHash { get; init; }

    /// <summary>
    /// Indicates if this version is a beta/pre-release.
    /// </summary>
    public bool IsPreRelease { get; init; }
}
