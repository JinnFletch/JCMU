using JinnDev.Utilities.Monad;

namespace JinnDev.JCMU.AddonManager.Interfaces;

/// <summary>
/// The primary orchestrator that glues the Source and Builder together to execute user commands.
/// </summary>
public interface IAddonInstaller
{
    /// <summary>
    /// Executes the full pipeline: Downloads source, compiles it, validates the manifest, 
    /// and moves the final binaries to the protected Plugin directory.
    /// </summary>
    /// <param name="source">The source provider to pull the code from.</param>
    /// <param name="addonId">The requested addon identifier.</param>
    /// <param name="version">The requested version (or "latest" if null).</param>
    /// <returns>A parameterless monad representing the success or failure of the installation.</returns>
    Task<Maybe> InstallAsync(IAddonSource source, string addonId, string? version = null);

    /// <summary>
    /// Safely removes an installed addon from the local filesystem and unregisters its context menus.
    /// </summary>
    /// <param name="addonId">The identifier of the addon to remove.</param>
    /// <returns>A parameterless monad representing the success or failure of the uninstallation.</returns>
    Task<Maybe> UninstallAsync(string addonId);
}