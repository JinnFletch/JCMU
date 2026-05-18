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

            var targetDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "JCMU", "Plugins", addonId);

            if (Directory.Exists(targetDirectory))
            {
                Directory.Delete(targetDirectory, true);
                _logger.LogInformation("Successfully deleted addon files.");

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
            else
            {
                _logger.LogWarning("Addon directory not found: {Path}", targetDirectory);
            }

            // Delete the configuration and secrets ---
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
            .BindAsync(match => source.GetVersionsAsync(match.RepositoryUrl)
                .BindAsync(versions =>
                {
                    if (versions.Count == 0) return Maybe.None<InstallContext>("Repository found, but it has no tags/releases.");

                    var selected = string.IsNullOrWhiteSpace(targetVersion)
                        ? versions.OrderByDescending(v => v.VersionName).First()
                        : versions.FirstOrDefault(v => v.VersionName.Equals(targetVersion, StringComparison.OrdinalIgnoreCase));

                    if (selected == null) return Maybe.None<InstallContext>($"Version '{targetVersion}' not found.");

                    return Maybe.Some(FileSystemManager.CreateInstallContext(match.AddonId, selected.VersionName, match.RepositoryUrl));
                })
            ).ConfigureAwait(false);
    }
}