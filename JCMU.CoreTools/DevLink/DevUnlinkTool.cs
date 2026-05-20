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

    /// <param name="targetDirectory">In the case of Unlink, this parameter is treated as the AddonId.</param>
    public Task<Maybe> ExecuteAsync(string targetDirectory)
    {
        return Task.FromResult(Maybe.Try(() =>
        {
            var addonId = targetDirectory.Trim();
            if (string.IsNullOrWhiteSpace(addonId))
                return Maybe.Fail("No Addon ID provided for unlinking.");

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