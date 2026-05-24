using JinnDev.JCMU.AddonManager.Interfaces;
using JinnDev.JCMU.AddonManager.Models;
using JinnDev.Utilities.Monad;
using System.Runtime.Versioning;

namespace JinnDev.JCMU.CoreTools.DevLink;

[SupportedOSPlatform("windows")]
public class DevUnlinkTool : ICoreTool
{
    private readonly IRegistryManager _registryManager;

    private static readonly string PluginsBase = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "JCMU", "Plugins");

    public string ToolId => "Core.DevUnlink";

    public MenuDefinition Menu => new MenuDefinition
    {
        // Note: We likely won't show this in the right-click menu because it targets an AddonId,
        // but it's here to satisfy the ICoreTool interface.
        MenuItemName = "Remove Dev Link",
        IconPath = "imageres.dll,-89", // Trash/Remove icon
        Ordinal = 999,
        RunInBackground = false
    };

    public DevUnlinkTool(IRegistryManager registryManager)
    {
        _registryManager = registryManager;
    }

    /// <param name="targetDirectory">In the case of Unlink, this parameter is treated as the AddonId, OR the directory if invoked via context menu.</param>
    public Task<Maybe> ExecuteAsync(string targetDirectory)
    {
        return Task.FromResult(Maybe.Try(() =>
        {
            var input = targetDirectory.Trim('"').Trim();
            if (string.IsNullOrWhiteSpace(input))
                return Maybe.Fail("No Addon ID or directory provided for unlinking.");

            string addonId = input;

            // If the input is a directory (e.g. from the Right-Click Menu), try to infer the AddonId
            if (Directory.Exists(input))
            {
                var manifests = Directory.GetFiles(input, "manifest.json", SearchOption.AllDirectories);
                if (manifests.Length > 0)
                {
                    try
                    {
                        // Use the first manifest found to extract the AddonId
                        var json = File.ReadAllText(manifests[0]);
                        var manifest = System.Text.Json.JsonSerializer.Deserialize<PluginManifest>(
                            json, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                        if (manifest != null && !string.IsNullOrWhiteSpace(manifest.AddonId))
                        {
                            addonId = manifest.AddonId;
                        }
                    }
                    catch
                    {
                        // If it fails to parse, we will let it fall through and likely fail the AddonId validation below
                    }
                }
                else if (Path.IsPathRooted(input))
                {
                    // It was definitely an absolute path, but no manifest was found
                    return Maybe.Fail($"The directory '{input}' does not contain a manifest.json. Cannot determine Addon ID.");
                }
            }

            // Safety check to ensure we aren't about to treat a local file path as an AddonId
            if (Path.IsPathRooted(addonId) || addonId.Contains('\\') || addonId.Contains('/'))
            {
                return Maybe.Fail($"'{addonId}' is not a valid Addon ID. If you right-clicked a directory, ensure it contains a valid manifest.json.");
            }

            Console.WriteLine($"\n--- Unlinking Dev Addon: {addonId} ---");

            // Search the 2-tier structure
            var targetDirectories = Directory.Exists(PluginsBase)
                ? Directory.GetDirectories(PluginsBase)
                    .SelectMany(authorDir => Directory.GetDirectories(authorDir))
                    .Where(addonDir => Path.GetFileName(addonDir).Equals(addonId, StringComparison.OrdinalIgnoreCase))
                    .ToList()
                : new List<string>();

            // Legacy fallback
            var legacyPath = Path.Combine(PluginsBase, addonId);
            if (Directory.Exists(legacyPath) && !targetDirectories.Contains(legacyPath))
                targetDirectories.Add(legacyPath);

            if (targetDirectories.Count == 0)
                return Maybe.Fail($"No dev-linked addon found with ID '{addonId}'.");

            if (targetDirectories.Count > 1)
                return Maybe.Fail($"Ambiguous dev-link: Multiple addons found with ID '{addonId}'. Please manually clean your Plugins directory.");

            var targetPath = targetDirectories[0];

            // Protect against accidentally deleting normal installations
            var dirInfo = new DirectoryInfo(targetPath);
            if (dirInfo.LinkTarget == null)
            {
                return Maybe.Fail($"The addon '{addonId}' is installed normally, not Dev-Linked. Use 'jcmu uninstall {addonId}' instead.");
            }

            // 1. Unregister from Windows Explorer Registry
            var registryResult = _registryManager.UnregisterAddon(addonId);
            if (!registryResult.HasValue)
                return registryResult;

            // 2. Remove the Directory Junction
            // Safety: recursive=false ensures we only delete the link, NOT the developer's source code!
            Directory.Delete(targetPath, recursive: false);

            // 3. Clean up the {Author} folder if it is now empty
            var parent = Path.GetDirectoryName(targetPath);
            if (parent != null &&
                !parent.EndsWith("Plugins", StringComparison.OrdinalIgnoreCase) &&
                Directory.Exists(parent) &&
                !Directory.EnumerateFileSystemEntries(parent).Any())
            {
                Directory.Delete(parent);
            }

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"\n[SUCCESS] Dev-Link for '{addonId}' has been removed.");
            Console.ResetColor();

            return Maybe.SUCCESS;
        }));
    }
}