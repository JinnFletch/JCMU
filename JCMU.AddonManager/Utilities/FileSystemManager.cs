using System.Text.Json;
using JinnDev.JCMU.AddonManager.Models;
using JinnDev.JCMU.SDK.Models;
using JinnDev.Utilities.Monad;

namespace JinnDev.JCMU.AddonManager.Utilities;

/// <summary>
/// A monadic utility for handling secure I/O operations, directory state management, 
/// and context generation for the JCMU Addon Manager.
/// </summary>
internal static class FileSystemManager
{
    private static readonly string BaseDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "JCMU");

    /// <summary>
    /// Generates the immutable installation context containing all required absolute paths.
    /// </summary>
    public static InstallContext CreateInstallContext(string addonId, string version, string repositoryUrl)
    {
        var tempBase = Path.Combine(BaseDirectory, "Temp", $"{addonId}_{Guid.NewGuid().ToString()[..8]}");

        return new InstallContext
        {
            TargetAddonId = addonId,
            SelectedVersion = version,
            RepositoryUrl = repositoryUrl,
            TempCloneDirectory = Path.Combine(tempBase, "Source"),
            TempPublishDirectory = Path.Combine(tempBase, "Publish"),
            FinalPluginDirectory = Path.Combine(BaseDirectory, "Plugins", addonId)
        };
    }

    /// <summary>
    /// Safely creates the necessary temporary directories, wiping them first if they somehow exist.
    /// </summary>
    public static async Task<Maybe<InstallContext>> PrepareTempDirectoriesAsync(InstallContext context)
    {
        return Maybe.Try<InstallContext>(() =>
        {
            if (Directory.Exists(context.TempCloneDirectory))
                Directory.Delete(context.TempCloneDirectory, true);

            if (Directory.Exists(context.TempPublishDirectory))
                Directory.Delete(context.TempPublishDirectory, true);

            Directory.CreateDirectory(context.TempCloneDirectory);
            Directory.CreateDirectory(context.TempPublishDirectory);

            return context;
        });
    }

    /// <summary>
    /// Verifies that the compiled output contains a valid manifest.json.
    /// </summary>
    public static async Task<Maybe<InstallContext>> ValidateCompiledManifestAsync(InstallContext ctx)
    {
        return await Maybe.TryAsync<InstallContext>(async () =>
        {
            var manifestPath = Path.Combine(ctx.TempPublishDirectory, "manifest.json");
            if (!File.Exists(manifestPath))
                throw new FileNotFoundException("The compiled output is missing a 'manifest.json' file. This is not a valid JCMU Addon.");

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

            var json = await File.ReadAllTextAsync(manifestPath).ConfigureAwait(false);
            var manifest = JsonSerializer.Deserialize<PluginManifest>(json, options)
                           ?? throw new Exception("Failed to deserialize manifest.json.");

            var validation = manifest.Validate();
            if (!validation.HasValue)
                throw new Exception($"Manifest validation failed: {validation.Message}");

            return ctx;
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// Safely moves the compiled DLLs from the temp directory to the final ProgramData plugin directory.
    /// </summary>
    public static async Task<Maybe<InstallContext>> MoveToFinalDestinationAsync(InstallContext context)
    {
        return Maybe.Try<InstallContext>(() =>
        {
            if (Directory.Exists(context.FinalPluginDirectory))
                Directory.Delete(context.FinalPluginDirectory, true);

            Directory.CreateDirectory(Path.GetDirectoryName(context.FinalPluginDirectory)!);
            Directory.Move(context.TempPublishDirectory, context.FinalPluginDirectory);

            return context;
        });
    }

    /// <summary>
    /// Best-effort cleanup of temporary directories. Does not fail the pipeline if a file is locked.
    /// </summary>
    public static async Task<Maybe> CleanupTempDirectoriesAsync(InstallContext context)
    {
        return Maybe.Try(() =>
        {
            var tempBase = Path.GetDirectoryName(context.TempCloneDirectory);
            if (tempBase != null && Directory.Exists(tempBase))
            {
                ForceDeleteDirectory(tempBase);
            }
        });
    }

    // Strips read-only attributes before deleting
    private static void ForceDeleteDirectory(string targetDir)
    {
        var directory = new DirectoryInfo(targetDir);

        // Remove ReadOnly attributes from all files and subdirectories
        foreach (var info in directory.GetFileSystemInfos("*", SearchOption.AllDirectories))
        {
            info.Attributes &= ~FileAttributes.ReadOnly;
        }

        directory.Delete(true);
    }
}