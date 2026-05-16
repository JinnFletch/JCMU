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

            // Resolve the absolute path to the icon file sitting next to the EXE
            var exeDirectory = Path.GetDirectoryName(Environment.ProcessPath) ?? "";
            var iconPath = Path.Combine(exeDirectory, "Icons", "jinn.ico");

            // Fallback to a default Windows icon if the custom one is missing
            var finalIconValue = File.Exists(iconPath) ? iconPath : "shell32.dll,-16764";

            // 1. Create the primary UI Hooks in Explorer
            foreach (var hookPath in _explorerHookPaths)
            {
                using var hookKey = WinReg.CurrentUser.CreateSubKey($@"{hookPath}\JCMU_Core");
                hookKey.SetValue("MUIVerb", "JinnCM");
                hookKey.SetValue("ExtendedSubCommandsKey", "JCMU_Menu");

                // Set the Icon
                hookKey.SetValue("Icon", finalIconValue);
            }

            // 2. Create the hidden backing store for the Root placement
            // This is where Addons with MenuPlacement.Root will be written by the RegistryManager
            using var rootStore = WinReg.CurrentUser.CreateSubKey(@"Software\Classes\JCMU_Menu\shell");

            // 3. Pre-create the placement subgroups
            CreateSubgroupAnchor(rootStore, "001_GitTools", "Git Tools", "JCMU_Menu_GitTools");
            CreateSubgroupAnchor(rootStore, "002_FileSystem", "File System", "JCMU_Menu_FileSystem");
            CreateSubgroupAnchor(rootStore, "003_CodeGeneration", "Code Gen", "JCMU_Menu_CodeGeneration");

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

            // Remove the Backing Stores
            using var classesKey = WinReg.CurrentUser.OpenSubKey(@"Software\Classes", writable: true);
            if (classesKey != null)
            {
                classesKey.DeleteSubKeyTree("JCMU_Menu", throwOnMissingSubKey: false);
                classesKey.DeleteSubKeyTree("JCMU_Menu_GitTools", throwOnMissingSubKey: false);
                classesKey.DeleteSubKeyTree("JCMU_Menu_FileSystem", throwOnMissingSubKey: false);
                classesKey.DeleteSubKeyTree("JCMU_Menu_CodeGeneration", throwOnMissingSubKey: false);
            }

            _logger.LogInformation("Core registry teardown successful.");
        });
    }

    /// <summary>
    /// Helper method to create a cascading sub-menu anchor.
    /// </summary>
    private static void CreateSubgroupAnchor(Microsoft.Win32.RegistryKey parentKey, string keyName, string displayName, string targetExtendedKey)
    {
        using var subgroupKey = parentKey.CreateSubKey(keyName);
        subgroupKey.SetValue("MUIVerb", displayName);
        subgroupKey.SetValue("ExtendedSubCommandsKey", targetExtendedKey);

        // Ensure the actual target backing store exists so the RegistryManager can write to it later
        using var _ = WinReg.CurrentUser.CreateSubKey($@"Software\Classes\{targetExtendedKey}\shell");
    }
}