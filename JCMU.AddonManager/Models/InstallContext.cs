namespace JinnDev.JCMU.AddonManager.Models;

/// <summary>
/// The state object passed down the monadic pipeline during the installation process.
/// </summary>
public record InstallContext
{
    /// <summary>
    /// The unique identifier of the addon being installed.
    /// </summary>
    public required string TargetAddonId { get; init; }

    /// <summary>
    /// The specific version being pulled.
    /// </summary>
    public required string SelectedVersion { get; init; }

    /// <summary>
    /// The Git URL to clone from.
    /// </summary>
    public required string RepositoryUrl { get; init; }

    /// <summary>
    /// The absolute path where the raw source code will be temporarily cloned.
    /// </summary>
    public required string TempCloneDirectory { get; init; }

    /// <summary>
    /// The absolute path where 'dotnet publish' will output the compiled DLLs temporarily.
    /// </summary>
    public required string TempPublishDirectory { get; init; }

    /// <summary>
    /// The final, secure absolute path (e.g., in ProgramData) where the compiled addon will reside.
    /// </summary>
    public required string FinalPluginDirectory { get; init; }
}
