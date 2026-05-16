using System.Runtime.Versioning;
using JinnDev.Utilities.Monad;
using Microsoft.Extensions.Logging;
using WinReg = Microsoft.Win32.Registry;

namespace JinnDev.JCMU.ConsoleBed.Registry;

[SupportedOSPlatform("windows")]
public class CoreRegistrar
{
    private readonly ILogger<CoreRegistrar> _logger;

    // The primary hook points in Windows Explorer
    private readonly string[] _explorerHookPaths =
    {
        @"Software\Classes\Directory\Background\shell", // Right-click empty space in a folder
        @"Software\Classes\Directory\shell"             // Right-click a folder icon
    };

    public CoreRegistrar(ILogger<CoreRegistrar> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Initializes the Core registry structure. This creates the main "JinnCM" context menu 
    /// and prepares the cascading subgroups (Git Tools, Code Gen, etc.).
    /// </summary>
    public Maybe InitializeCore()
    {
        return Maybe.Try(() =>
        {
            _logger.LogInformation("Initializing JCMU Core Registry Anchors...");

            var exeDirectory = Path.GetDirectoryName(Environment.ProcessPath) ?? "";
            var coreExePath = Environment.ProcessPath ?? throw new Exception("Cannot determine process path.");
            var iconPath = Path.Combine(exeDirectory, "Icons", "jinn.ico");

            var finalIconValue = File.Exists(iconPath) ? iconPath : "shell32.dll,-16764";

            // 1. Create the primary UI Hooks in Explorer
            foreach (var hookPath in _explorerHookPaths)
            {
                using var hookKey = WinReg.CurrentUser.CreateSubKey($@"{hookPath}\JCMU_Core");
                hookKey.SetValue("MUIVerb", "JinnCM");
                hookKey.SetValue("ExtendedSubCommandsKey", "JCMU_Menu");
                hookKey.SetValue("Icon", finalIconValue);
            }

            // 2. Create the hidden backing store for the Root placement
            using var rootStore = WinReg.CurrentUser.CreateSubKey(@"Software\Classes\JCMU_Menu\shell");

            // 3. Bake in the Permanent "Search for Addons" Menu Item
            // Change "999_" to "z_" to ensure it sorts after "JCMU_Category_..."
            using var searchItemKey = rootStore.CreateSubKey("z_SearchAddons");
            searchItemKey.SetValue("MUIVerb", "Search for Addons...");
            searchItemKey.SetValue("Icon", "imageres.dll,-177");

            // This flag 0x20 is what creates that Horizontal Rule (Separator)
            searchItemKey.SetValue("CommandFlags", 0x20, Microsoft.Win32.RegistryValueKind.DWord);

            using var searchCmdKey = searchItemKey.CreateSubKey("command");
            searchCmdKey.SetValue("", $"\"{coreExePath}\" -i search");

            _logger.LogInformation("Core registry initialization successful.");
        });
    }

    /// <summary>
    /// Cleans up all Core registry hooks and deletes the JCMU tree. 
    /// Use this if you are completely uninstalling JCMU from your machine.
    /// </summary>
    public Maybe TeardownCore()
    {
        return Maybe.Try(() =>
        {
            _logger.LogInformation("Tearing down JCMU Core Registry Anchors...");

            // Remove the UI Hooks
            foreach (var hookPath in _explorerHookPaths)
            {
                using var hookKey = WinReg.CurrentUser.OpenSubKey(hookPath, writable: true);
                hookKey?.DeleteSubKeyTree("JCMU_Core", throwOnMissingSubKey: false);
            }

            // Remove all generated Backing Stores
            using var classesKey = WinReg.CurrentUser.OpenSubKey(@"Software\Classes", writable: true);
            if (classesKey != null)
            {
                // Delete the root
                classesKey.DeleteSubKeyTree("JCMU_Menu", throwOnMissingSubKey: false);

                // Delete all dynamic categories created by addons
                var dynamicKeys = classesKey.GetSubKeyNames()
                    .Where(k => k.StartsWith("JCMU_Category_", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                foreach (var key in dynamicKeys)
                {
                    classesKey.DeleteSubKeyTree(key, throwOnMissingSubKey: false);
                }
            }

            _logger.LogInformation("Core registry teardown successful.");
        });
    }
}