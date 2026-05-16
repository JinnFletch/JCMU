using JinnDev.Utilities.Monad;
using JinnDev.JCMU.AddonManager.Models;

namespace JinnDev.JCMU.AddonManager.Interfaces;

/// <summary>
/// Defines a remote registry or "Store" where JCMU Addons can be discovered and downloaded.
/// </summary>
public interface IAddonSource
{
    /// <summary>
    /// Searches the remote source for addons matching the query.
    /// </summary>
    /// <param name="query">The search term provided by the user.</param>
    /// <param name="page">In case of manu search results, which page number to find</param>
    /// <returns>A monad containing a list of search results, or a failure if the network/API call fails.</returns>
    Task<Maybe<AddonPagedResult>> SearchAsync(string? query, int page = 1);

    /// <summary>
    /// Retrieves all available versions (tags) for a specific addon repository.
    /// </summary>
    /// <param name="repositoryUrl">The Git URL of the addon.</param>
    /// <returns>A monad containing the list of available versions.</returns>
    Task<Maybe<IReadOnlyList<AddonVersionInfo>>> GetVersionsAsync(string repositoryUrl);

    /// <summary>
    /// Executes a Git Clone to pull the specific version of the source code to the local disk.
    /// </summary>
    /// <param name="repositoryUrl">The Git URL to clone.</param>
    /// <param name="version">The specific tag or branch to checkout.</param>
    /// <param name="targetTempPath">The local directory to clone the code into.</param>
    /// <returns>A monad containing the absolute path to the downloaded source directory.</returns>
    Task<Maybe> DownloadSourceAsync(string repositoryUrl, string version, string targetTempPath);
}
