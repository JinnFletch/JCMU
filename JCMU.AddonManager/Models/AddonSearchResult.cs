namespace JinnDev.JCMU.AddonManager.Models;

/// <summary>
/// Represents a high-level overview of an addon found in a remote source repository.
/// </summary>
public record AddonSearchResult
{
    /// <summary>
    /// The unique identifier of the addon (e.g., "JCMU.CleanVSBS").
    /// </summary>
    public required string AddonId { get; init; }

    /// <summary>
    /// The Git repository URL where the source code is hosted.
    /// </summary>
    public required string RepositoryUrl { get; init; }

    /// <summary>
    /// A brief explanation of the addon's functionality.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// The author or organization that published the addon.
    /// </summary>
    public string? Author { get; init; }

    /// <summary>
    /// Categories or keywords associated with the addon for easier searching.
    /// </summary>
    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();
}
