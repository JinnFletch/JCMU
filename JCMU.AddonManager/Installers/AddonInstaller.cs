using JinnDev.JCMU.AddonManager.Interfaces;
using JinnDev.JCMU.AddonManager.Models;
using JinnDev.JCMU.AddonManager.Security;
using JinnDev.JCMU.AddonManager.Utilities;
using JinnDev.Utilities.Monad;
using Microsoft.Extensions.Logging;

namespace JinnDev.JCMU.AddonManager.Installers;

/// <summary>
/// The primary orchestrator responsible for the full installation pipeline of a JCMU Addon.
/// </summary>
public class AddonInstaller : IAddonInstaller
{
    private readonly IAddonBuilder _builder;
    private readonly ITrustManager _trustManager;
    private readonly ILogger<AddonInstaller> _logger;

    public AddonInstaller(IAddonBuilder builder, ITrustManager trustManager, ILogger<AddonInstaller> logger)
    {
        _builder = builder;
        _trustManager = trustManager;
        _logger = logger;
    }

    public async Task<Maybe<string>> InstallAsync(IAddonSource source, string addonId, string? version = null)
    {
        _logger.LogInformation("Beginning installation pipeline for '{AddonId}'...", addonId);

        // Phase 1: Context Building (Network & Setup)
        return await DetermineInstallContextAsync(source, addonId, version)
            .BindAsync(FileSystemManager.PrepareTempDirectoriesAsync)

            // === NAMESPACE & ANTI-HIJACKING CHECK ===
            .BindAsync(ctx =>
            {
                var pluginsBase = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "JCMU", "Plugins");
                if (Directory.Exists(pluginsBase))
                {
                    // 1. Find any existing installation of this AddonId (checks 1-tier and 2-tier)
                    var existingDirs = Directory.GetDirectories(pluginsBase)
                        .SelectMany(authorDir => Directory.GetDirectories(authorDir))
                        .Concat(Directory.GetDirectories(pluginsBase))
                        .Where(addonDir => Path.GetFileName(addonDir).Equals(ctx.TargetAddonId, StringComparison.OrdinalIgnoreCase))
                        .Distinct()
                        .ToList();

                    if (existingDirs.Any())
                    {
                        var existingPath = existingDirs.First();

                        // Extract Author from folder structure: Plugins\{Author}\{AddonId}
                        var existingAuthor = Path.GetFileName(Path.GetDirectoryName(existingPath));
                        var targetAuthor = Path.GetFileName(Path.GetDirectoryName(ctx.FinalPluginDirectory));

                        bool isLegacy = existingAuthor!.Equals("Plugins", StringComparison.OrdinalIgnoreCase);
                        bool isSameAuthor = !isLegacy && string.Equals(existingAuthor, targetAuthor, StringComparison.OrdinalIgnoreCase);

                        // If it's a legacy install, we can't verify the author safely.
                        if (isLegacy)
                        {
                            return Maybe.None<InstallContext>(
                                $"LEGACY COLLISION: '{ctx.TargetAddonId}' is installed using an older JCMU structure. " +
                                $"Please run 'jcmu uninstall {ctx.TargetAddonId}' first to upgrade to the new secure format.");
                        }

                        // If the authors DON'T match, this is a Hijacking attempt. BLOCK.
                        if (!isSameAuthor)
                        {
                            return Maybe.None<InstallContext>(
                                $"NAMESPACE COLLISION: '{ctx.TargetAddonId}' is already installed by author '{existingAuthor}'. " +
                                $"To protect your system, you cannot install a version by '{targetAuthor}' over it.");
                        }

                        // If it IS the same author, we do nothing and let the pipeline continue.
                        // The MoveToFinalDestinationAsync step will safely overwrite the files.
                    }
                }
                return Maybe.Some(ctx);
            })
            // ========================================

            // Phase 2: Acquisition & Compilation (Git & DotNet)
            .BindAsync(ctx => source.DownloadSourceAsync(ctx.RepositoryUrl, ctx.SelectedVersion, ctx.TempCloneDirectory).WithValueAsync(ctx))
            .BindAsync(ctx => _builder.BuildPluginAsync(ctx.TempCloneDirectory, ctx.TempPublishDirectory).WithValueAsync(ctx))

            // Phase 3: Validation & Deployment (Disk I/O)
            .BindAsync(FileSystemManager.ValidateCompiledManifestAsync)
            .BindAsync(FileSystemManager.MoveToFinalDestinationAsync)

            .TapAsync(
                someActionAsync: async ctx =>
                {
                    _logger.LogInformation("Successfully installed {AddonId} v{Version}.", ctx.TargetAddonId, ctx.SelectedVersion);
                    await FileSystemManager.CleanupTempDirectoriesAsync(ctx).ConfigureAwait(false);
                },
                noneActionAsync: async none => _logger.LogError(none.Exception, "Installation failed: {Message}", none.Message)
            )

            // MAP the Context to the final directory string so the Core can use it
            .MapAsync(ctx => ctx.FinalPluginDirectory)
            .ConfigureAwait(false);
    }

    public async Task<Maybe> UninstallAsync(string addonId)
    {
        return await Maybe.TryAsync(async () =>
        {
            _logger.LogInformation("Attempting to uninstall '{AddonId}'...", addonId);

            var pluginsBase = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "JCMU", "Plugins");

            // Search the 2-tier structure for the specific AddonId
            var targetDirectories = Directory.Exists(pluginsBase)
                ? Directory.GetDirectories(pluginsBase)
                    .SelectMany(authorDir => Directory.GetDirectories(authorDir))
                    .Where(addonDir => Path.GetFileName(addonDir).Equals(addonId, StringComparison.OrdinalIgnoreCase))
                    .ToList()
                : new List<string>();

            // Legacy fallback (just in case they are uninstalling an older 1-tier addon)
            var legacyPath = Path.Combine(pluginsBase, addonId);
            if (Directory.Exists(legacyPath) && !targetDirectories.Contains(legacyPath))
                targetDirectories.Add(legacyPath);

            if (targetDirectories.Count == 0)
            {
                _logger.LogWarning("Addon directory not found: {AddonId}", addonId);
            }
            else
            {
                foreach (var targetDirectory in targetDirectories)
                {
                    Directory.Delete(targetDirectory, true);
                    _logger.LogInformation("Successfully deleted addon files at {Path}.", targetDirectory);

                    // Deletes the {Author} folder if it's now empty
                    var parent = Path.GetDirectoryName(targetDirectory);
                    while (parent != null &&
                           !parent.EndsWith("Plugins", StringComparison.OrdinalIgnoreCase) &&
                           Directory.Exists(parent) &&
                           !Directory.EnumerateFileSystemEntries(parent).Any())
                    {
                        Directory.Delete(parent);
                        parent = Path.GetDirectoryName(parent);
                    }
                }
            }

            // Delete the configuration and secrets
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var configFilePath = Path.Combine(localAppData, "JCMU", "Configs", $"{addonId}.json");

            if (File.Exists(configFilePath))
            {
                File.Delete(configFilePath);
                _logger.LogInformation("Successfully deleted addon configuration and secrets.");
            }
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// Reaches out to the source to find the repository URL and resolve the correct version tag.
    /// </summary>
    private async Task<Maybe<InstallContext>> DetermineInstallContextAsync(IAddonSource source, string addonId, string? targetVersion)
    {
        return await source.SearchAsync(addonId)
            .BindAsync(results =>
            {
                var match = results.Items.FirstOrDefault(r => r.AddonId.Equals(addonId, StringComparison.OrdinalIgnoreCase));
                if (match == null) return Maybe.None<AddonSearchResult>($"Could not find an addon matching ID: {addonId}");

                // SECURITY ENFORCEMENT
                if (!_trustManager.IsTrusted(match.Author))
                {
                    return Maybe.None<AddonSearchResult>($"Installation blocked: Author '{match.Author}' is not trusted.\nRun 'jcmu trust {match.Author}' to allow installation.");
                }

                return Maybe.Some(match);
            })
            .BindAsync(match => source.GetRemoteManifestAsync(match.RepositoryUrl)
                .BindAsync(manifest =>
                {
                    if (!Uri.TryCreate(match.RepositoryUrl, UriKind.Absolute, out var uri) || uri.Segments.Length < 3)
                        return Maybe.None<AddonSearchResult>($"Invalid repository URL: {match.RepositoryUrl}");

                    var owner = uri.Segments[1].Trim('/');

                    // 1. Anti-Spoofing: Author MUST match GitHub Owner
                    if (!string.Equals(manifest.Author, owner, StringComparison.OrdinalIgnoreCase))
                    {
                        return Maybe.None<AddonSearchResult>(
                            $"SPOOFING DETECTED: The manifest claims to be authored by '{manifest.Author}', " +
                            $"but the repository is owned by '{owner}'. Installation aborted to protect your system.");
                    }

                    // 2. Identity Verification: AddonId must match
                    // Using EndsWith to safely bridge the gap before Step 4 is implemented (since search currently returns Owner/Repo)
                    if (!addonId.EndsWith(manifest.AddonId, StringComparison.OrdinalIgnoreCase))
                    {
                        return Maybe.None<AddonSearchResult>(
                            $"IDENTITY MISMATCH: You requested '{addonId}', but the repository manifest identifies as '{manifest.AddonId}'.");
                    }

                    return Maybe.Some(match);
                })
            )
            .BindAsync(match => source.GetVersionsAsync(match.RepositoryUrl)
                .BindAsync(versions =>
                {
                    if (versions.Count == 0) return Maybe.None<InstallContext>("Repository found, but it has no tags/releases.");

                    var selected = string.IsNullOrWhiteSpace(targetVersion)
                        ? versions.OrderByDescending(v => v.VersionName).First()
                        : versions.FirstOrDefault(v => v.VersionName.Equals(targetVersion, StringComparison.OrdinalIgnoreCase));

                    if (selected == null) return Maybe.None<InstallContext>($"Version '{targetVersion}' not found.");

                    return Maybe.Some(FileSystemManager.CreateInstallContext(match.AddonId, selected.VersionName, match.RepositoryUrl, match.Author!));
                })
            ).ConfigureAwait(false);
    }
}