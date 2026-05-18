using JinnDev.JCMU.AddonManager.Interfaces;
using JinnDev.JCMU.CoreTools;
using JinnDev.Utilities.Monad;
using Microsoft.Extensions.Logging;
using System.Runtime.Versioning;
using WinReg = Microsoft.Win32.Registry;

namespace JinnDev.JCMU.ConsoleBed.Registry;

[SupportedOSPlatform("windows")]
public class CoreRegistrar
{
    private readonly IRegistryManager _registryManager;
    private readonly IEnumerable<ICoreTool> _coreTools;
    private readonly ILogger<CoreRegistrar> _logger;

    // The primary hook points in Windows Explorer
    private readonly string[] _explorerHookPaths =
    {
        @"Software\Classes\Directory\Background\shell", // Right-click empty space in a folder
        @"Software\Classes\Directory\shell"             // Right-click a folder icon
    };

    public CoreRegistrar(IRegistryManager registryManager, IEnumerable<ICoreTool> coreTools, ILogger<CoreRegistrar> logger)
    {
        _registryManager = registryManager;
        _coreTools = coreTools;
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

            foreach (var hookPath in _explorerHookPaths)
            {
                using var hookKey = WinReg.CurrentUser.CreateSubKey($@"{hookPath}\JCMU_Core");
                hookKey.SetValue("MUIVerb", "JinnCM");
                hookKey.SetValue("ExtendedSubCommandsKey", "JCMU_Menu");
                hookKey.SetValue("Icon", finalIconValue);
            }

            using var rootStore = WinReg.CurrentUser.CreateSubKey(@"Software\Classes\JCMU_Menu\shell");

            // Automatically write all injected Core Tools!
            foreach (var tool in _coreTools)
            {
                var modifiedMenu = tool.Menu with { Category = "JCMU Tools" };
                var result = _registryManager.RegisterAddon(tool.ToolId, modifiedMenu, coreExePath, isCoreTool: true);

                if (!result.HasValue)
                    _logger.LogWarning("Failed to register built-in tool {ToolId}: {Message}", tool.ToolId, result.Message);
            }

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

                classesKey.DeleteSubKeyTree("JCMU_Tools", throwOnMissingSubKey: false);

                // Delete all dynamic categories created by addons
                var dynamicKeys = classesKey.GetSubKeyNames()
                    .Where(k => k.StartsWith("JCMU_Category_", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                foreach (var key in dynamicKeys)
                {
                    classesKey.DeleteSubKeyTree(key, throwOnMissingSubKey: false);
                }
            }
        });
    }
}