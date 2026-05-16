using JinnDev.JCMU.AddonManager.Models;
using JinnDev.Utilities.Monad;

namespace JinnDev.JCMU.ConsoleBed.Registry;

/// <summary>
/// Abstracts Windows Registry operations for installing and uninstalling dynamic context menus.
/// </summary>
public interface IRegistryManager
{
    /// <summary>
    /// Translates a MenuDefinition into Windows Registry keys to create a static context menu.
    /// </summary>
    /// <param name="addonId">The unique identifier of the addon.</param>
    /// <param name="menu">The menu structure defined by the addon.</param>
    /// <param name="coreExePath">The absolute path to JCMU.ConsoleBed.exe.</param>
    /// <returns>A monad representing the success or failure of the registry write.</returns>
    Maybe RegisterAddon(string addonId, MenuDefinition menu, string coreExePath);

    /// <summary>
    /// Removes all registry keys associated with the specified addon.
    /// </summary>
    /// <param name="addonId">The unique identifier of the addon.</param>
    /// <returns>A monad representing the success or failure of the registry cleanup.</returns>
    Maybe UnregisterAddon(string addonId);
}