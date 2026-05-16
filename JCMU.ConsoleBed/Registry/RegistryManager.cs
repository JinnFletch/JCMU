using System.Runtime.Versioning;
using JinnDev.JCMU.SDK.Models;
using JinnDev.Utilities.Monad;
using Microsoft.Extensions.Logging;
using WinReg = Microsoft.Win32.Registry;

namespace JinnDev.JCMU.ConsoleBed.Registry;

[SupportedOSPlatform("windows")]
public class RegistryManager : IRegistryManager
{
    private readonly ILogger<RegistryManager> _logger;

    // Base registry paths mapped to the SDK MenuPlacement enum.
    // The CoreRegistrar (Step 2) will create these root anchors.
    private static readonly Dictionary<MenuPlacement, string> PlacementPaths = new()
    {
        { MenuPlacement.Root, @"Software\Classes\JCMU_Menu\shell" },
        { MenuPlacement.GitTools, @"Software\Classes\JCMU_Menu_GitTools\shell" },
        { MenuPlacement.FileSystem, @"Software\Classes\JCMU_Menu_FileSystem\shell" },
        { MenuPlacement.CodeGeneration, @"Software\Classes\JCMU_Menu_CodeGeneration\shell" }
    };

    public RegistryManager(ILogger<RegistryManager> logger)
    {
        _logger = logger;
    }

    public Maybe RegisterAddon(string addonId, MenuDefinition menu, string coreExePath)
    {
        return Maybe.Try(() =>
        {
            _logger.LogInformation("Writing registry keys for Addon: {AddonId}", addonId);

            var basePath = PlacementPaths[menu.Placement];
            using Microsoft.Win32.RegistryKey baseKey = WinReg.CurrentUser.CreateSubKey(basePath);

            // Format the key name with Ordinal to ensure Windows sorts the menu correctly
            var rootKeyName = $"{menu.Ordinal:D3}_{addonId}";

            WriteMenuNode(baseKey, rootKeyName, menu, coreExePath, addonId);
        });
    }

    public Maybe UnregisterAddon(string addonId)
    {
        return Maybe.Try(() =>
        {
            _logger.LogInformation("Removing registry keys for Addon: {AddonId}", addonId);

            // 1. Delete from all standard placement directories
            foreach (var basePath in PlacementPaths.Values)
            {
                using var baseKey = WinReg.CurrentUser.OpenSubKey(basePath, writable: true);
                if (baseKey != null)
                {
                    // Find any key that ends with the addonId (ignoring the ordinal prefix)
                    var targetKey = baseKey.GetSubKeyNames()
                        .FirstOrDefault(k => k.EndsWith(addonId, StringComparison.OrdinalIgnoreCase));

                    if (targetKey != null)
                    {
                        baseKey.DeleteSubKeyTree(targetKey, throwOnMissingSubKey: false);
                    }
                }
            }

            // 2. Cleanup any generated ExtendedSubCommandsKeys (Nested menus)
            using var classesKey = WinReg.CurrentUser.OpenSubKey(@"Software\Classes", writable: true);
            if (classesKey != null)
            {
                var subCommandPrefix = $"JCMU_Sub_{addonId}";
                var orphanedKeys = classesKey.GetSubKeyNames()
                    .Where(k => k.StartsWith(subCommandPrefix, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                foreach (var orphan in orphanedKeys)
                {
                    classesKey.DeleteSubKeyTree(orphan, throwOnMissingSubKey: false);
                }
            }
        });
    }

    /// <summary>
    /// Recursively walks the MenuDefinition to create nested registry structures.
    /// </summary>
    private static void WriteMenuNode(Microsoft.Win32.RegistryKey parentKey, string keyName, MenuDefinition menu, string coreExePath, string addonId)
    {
        using var itemKey = parentKey.CreateSubKey(keyName);
        itemKey.SetValue("MUIVerb", menu.MenuItemName);

        if (!string.IsNullOrWhiteSpace(menu.IconPath))
        {
            itemKey.SetValue("Icon", menu.IconPath);
        }

        if (menu.SubItems != null && menu.SubItems.Any())
        {
            var extSubKeyName = $"JCMU_Sub_{addonId}_{keyName}";
            itemKey.SetValue("ExtendedSubCommandsKey", extSubKeyName);

            using var subRootKey = WinReg.CurrentUser.CreateSubKey($@"Software\Classes\{extSubKeyName}\shell");

            foreach (var child in menu.SubItems)
            {
                var childKeyName = $"{child.Ordinal:D3}_{Guid.NewGuid():N}";
                WriteMenuNode(subRootKey, childKeyName, child, coreExePath, addonId);
            }
        }
        else
        {
            using var cmdKey = itemKey.CreateSubKey("command");

            // [NEW LOGIC] Check the SDK property and append the background flag if necessary.
            var backgroundFlag = menu.RunInBackground ? " -b" : "";

            // Note: %V is passed to the executable as the final argument (TargetDirectory)
            var commandString = $"\"{coreExePath}\" execute {addonId}{backgroundFlag} \"%V\"";

            cmdKey.SetValue("", commandString);
        }
    }
}