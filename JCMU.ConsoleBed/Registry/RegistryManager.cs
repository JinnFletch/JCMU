using JinnDev.JCMU.AddonManager.Interfaces;
using JinnDev.JCMU.AddonManager.Models;
using JinnDev.JCMU.CoreTools;
using JinnDev.Utilities.Monad;
using Microsoft.Extensions.Logging;
using System.Runtime.Versioning;
using WinReg = Microsoft.Win32.Registry;

namespace JinnDev.JCMU.ConsoleBed.Registry;

[SupportedOSPlatform("windows")]
public class RegistryManager : IRegistryManager
{
    private readonly ILogger<RegistryManager> _logger;

    public RegistryManager(ILogger<RegistryManager> logger)
    {
        _logger = logger;
    }

    public Maybe RegisterAddon(string addonId, MenuDefinition menu, string coreExePath, bool isCoreTool = false)
    {
        return Maybe.Try(() =>
        {
            _logger.LogInformation("Writing registry keys for Addon: {AddonId}", addonId);

            // Determine Base Path dynamically
            var basePath = @"Software\Classes\JCMU_Menu\shell";

            if (!string.IsNullOrWhiteSpace(menu.Category))
            {
                basePath = EnsureDynamicCategoryExists(menu.Category);
            }

            using Microsoft.Win32.RegistryKey baseKey = WinReg.CurrentUser.CreateSubKey(basePath);

            // Format the key name with Ordinal to ensure Windows sorts the menu correctly
            var rootKeyName = $"{menu.Ordinal:D3}_{addonId}";

            WriteMenuNode(baseKey, rootKeyName, menu, coreExePath, addonId, isCoreTool);
        });
    }

    // Creates the folder anchor on the fly if it doesn't exist
    private static string EnsureDynamicCategoryExists(string categoryName)
    {
        var safeKey = new string(categoryName.Where(char.IsLetterOrDigit).ToArray());

        // Check if it's the Core Tools folder. If so, force it to the bottom with 'z_'
        bool isCoreTools = categoryName.Equals("JCMU Tools", StringComparison.OrdinalIgnoreCase);
        var categoryKeyName = isCoreTools ? "z_JCMUTools" : $"JCMU_Category_{safeKey}";

        using var rootStore = WinReg.CurrentUser.CreateSubKey(@"Software\Classes\JCMU_Menu\shell");
        using var anchorKey = rootStore.CreateSubKey(categoryKeyName);

        anchorKey.SetValue("MUIVerb", categoryName);
        anchorKey.SetValue("ExtendedSubCommandsKey", categoryKeyName);

        // Apply the special styling to the Core folder
        if (isCoreTools)
        {
            anchorKey.SetValue("Icon", "imageres.dll,-114"); // Gear icon
            anchorKey.SetValue("CommandFlags", 0x20, Microsoft.Win32.RegistryValueKind.DWord); // Horizontal Line
        }

        var categoryBackingStore = $@"Software\Classes\{categoryKeyName}\shell";
        using var _ = WinReg.CurrentUser.CreateSubKey(categoryBackingStore);

        return categoryBackingStore;
    }

    public Maybe UnregisterAddon(string addonId)
    {
        return Maybe.Try(() =>
        {
            _logger.LogInformation("Removing registry keys for Addon: {AddonId}", addonId);

            using var classesKey = WinReg.CurrentUser.OpenSubKey(@"Software\Classes", writable: true);
            if (classesKey == null) return;

            // 1. Cleanup from the Root Menu
            using var rootKey = classesKey.OpenSubKey(@"JCMU_Menu\shell", writable: true);
            if (rootKey != null)
            {
                DeleteKeysEndingWith(rootKey, addonId);
            }

            // 2. Cleanup from all Dynamic Categories
            var categoryKeys = classesKey.GetSubKeyNames()
                .Where(k => k.StartsWith("JCMU_Category_", StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var categoryName in categoryKeys)
            {
                using var categoryShellKey = classesKey.OpenSubKey($@"{categoryName}\shell", writable: true);
                if (categoryShellKey != null)
                {
                    DeleteKeysEndingWith(categoryShellKey, addonId);
                }

                // If the category is now empty, delete the category folder entirely!
                using var checkKey = classesKey.OpenSubKey($@"{categoryName}\shell");
                if (checkKey != null && checkKey.SubKeyCount == 0)
                {
                    classesKey.DeleteSubKeyTree(categoryName, throwOnMissingSubKey: false);
                    rootKey?.DeleteSubKeyTree(categoryName, throwOnMissingSubKey: false);
                }
            }

            // 3. Cleanup any generated ExtendedSubCommandsKeys (Nested menus)
            var subCommandPrefix = $"JCMU_Sub_{addonId}";
            var orphanedKeys = classesKey.GetSubKeyNames()
                .Where(k => k.StartsWith(subCommandPrefix, StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var orphan in orphanedKeys)
            {
                classesKey.DeleteSubKeyTree(orphan, throwOnMissingSubKey: false);
            }
        });
    }

    private static void DeleteKeysEndingWith(Microsoft.Win32.RegistryKey parentKey, string suffix)
    {
        var targets = parentKey.GetSubKeyNames()
            .Where(k => k.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var t in targets)
        {
            parentKey.DeleteSubKeyTree(t, throwOnMissingSubKey: false);
        }
    }

    /// <summary>
    /// Recursively walks the MenuDefinition to create nested registry structures.
    /// </summary>
    private static void WriteMenuNode(Microsoft.Win32.RegistryKey parentKey, string keyName, MenuDefinition menu, string coreExePath, string addonId, bool isCoreTool)
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
                WriteMenuNode(subRootKey, childKeyName, child, coreExePath, addonId, isCoreTool);
            }
        }
        else
        {
            using var cmdKey = itemKey.CreateSubKey("command");

            // --- THE MASTERY LOGIC ---
            string targetExe = coreExePath;

            if (menu.RunInBackground)
            {
                // Swap "jcmu.exe" for "jcmu-bg.exe"
                var directory = Path.GetDirectoryName(coreExePath)!;
                targetExe = Path.Combine(directory, "jcmu-bg.exe");
            }

            var backgroundFlag = menu.RunInBackground ? " -b" : "";

            // --- THE FIX ---
            // If it's a core tool, use the 'tool' verb. Otherwise, use 'execute'.
            var cliVerb = isCoreTool ? "tool" : "execute";
            var commandString = $"\"{targetExe}\" {cliVerb} {addonId}{backgroundFlag} \"%V\"";

            cmdKey.SetValue("", commandString);
        }
    }
}