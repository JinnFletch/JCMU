using JinnDev.Utilities.Monad;
using System.Text.RegularExpressions;

namespace JinnDev.JCMU.AddonManager.Models;

/// <summary>
/// Represents the identity, versioning, and core metadata of a JCMU Addon.
/// </summary>
public record PluginManifest
{
    /// <summary>
    /// A globally unique identifier for this addon (e.g., "JCMU.GitUtils"). 
    /// Used for folder naming and preventing installation collisions.
    /// </summary>
    public required string AddonId { get; init; }

    /// <summary>
    /// The user-friendly name of the addon displayed in CLI logs and tools.
    /// </summary>
    public required string DisplayName { get; init; }

    /// <summary>
    /// The semantic version of the addon (e.g., "1.0.4").
    /// </summary>
    public required string Version { get; init; }

    /// <summary>
    /// The author or organization that created the addon.
    /// </summary>
    public required string Author { get; init; }

    /// <summary>
    /// A brief explanation of what the addon does.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// The minimum version of the JCMU Core engine required to run this addon.
    /// </summary>
    public string? MinCoreVersion { get; init; }

    /// <summary>
    /// The URL to the plugin's github.  This is double checked by the framework.
    /// </summary>
    public string? RepositoryUrl { get; init; }

    /// <summary>
    /// Require the MenuDefinition so the core doesn't have to do reflection.
    /// </summary>
    public required MenuDefinition Menu { get; init; }

    /// <summary>
    /// Validates the manifest data, returning a successful Maybe if valid, 
    /// or a failed Maybe containing the specific missing property message.
    /// </summary>
    /// <returns>A parameterless monad representing the validation result.</returns>
    public Maybe Validate()
    {
        if (string.IsNullOrWhiteSpace(AddonId))
            return Maybe.Fail("AddonId cannot be null or whitespace.");

        if (!Regex.IsMatch(AddonId, @"^[a-zA-Z0-9\.\-_]+$"))
            return Maybe.Fail("AddonId can only contain letters, numbers, periods, hyphens, and underscores. No spaces.");

        if (string.IsNullOrWhiteSpace(DisplayName))
            return Maybe.Fail("DisplayName cannot be null or whitespace.");

        if (string.IsNullOrWhiteSpace(Version))
            return Maybe.Fail("Version cannot be null or whitespace.");

        if (Menu == null)
            return Maybe.Fail("Manifest is missing the 'Menu' definition.");

        return Maybe.SUCCESS;
    }
}
