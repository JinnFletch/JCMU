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

            var targetPath = Path.Combine(PluginsBase, addonId);

            if (!Directory.Exists(targetPath))
                return Maybe.Fail($"No dev-linked addon found with ID '{addonId}'.");

            // 1. Unregister from Windows Explorer Registry
            var registryResult = _registryManager.UnregisterAddon(addonId);
            if (!registryResult.HasValue)
                return registryResult;

            // 2. Remove the Directory Junction
            // Safety: recursive=false ensures we only delete the link, NOT the developer's source code!
            Directory.Delete(targetPath, recursive: false);

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"\n[SUCCESS] Dev-Link for '{addonId}' has been removed.");
            Console.ResetColor();

            return Maybe.SUCCESS;
        }));
    }
}