using System.Diagnostics;
using JinnDev.JCMU.AddonManager.Interfaces;
using JinnDev.JCMU.ConsoleBed.Registry;
using JinnDev.JCMU.ConsoleBed.Runtime;
using JinnDev.Utilities.Monad;
using Microsoft.Extensions.Logging;

namespace JinnDev.JCMU.ConsoleBed.Cli;

/// <summary>
/// Handles user-facing CLI commands related to Addon package management and menu registration.
/// </summary>
public class StoreCliDispatcher
{
    private readonly IAddonInstaller _installer;
    private readonly IAddonSource _source;
    private readonly IPluginLoader _loader;
    private readonly IRegistryManager _registryManager;
    private readonly ILogger<StoreCliDispatcher> _logger;

    public StoreCliDispatcher(
        IAddonInstaller installer,
        IAddonSource source,
        IPluginLoader loader,
        IRegistryManager registryManager,
        ILogger<StoreCliDispatcher> logger)
    {
        _installer = installer;
        _source = source;
        _loader = loader;
        _registryManager = registryManager;
        _logger = logger;
    }

    /// <summary>
    /// Executes the installation pipeline, extracts the Menu Definition, and registers it.
    /// Expected format: `jcmu install JCMU.CleanVSBS [optionalVersion]`
    /// </summary>
    public async Task<Maybe> HandleInstallAsync(string[] args)
    {
        if (args.Length < 2)
            return Maybe.Fail("Usage: jcmu install <AddonId> [Version]");

        var addonId = args[1];
        var version = args.Length > 2 ? args[2] : null;

        Console.WriteLine($"\n--- Installing Addon: {addonId} {(version != null ? $"[{version}]" : "[Latest]")} ---");

        var result = await _installer.InstallAsync(_source, addonId, version)
            .BindAsync(async finalDirectory =>
            {
                // Temporarily spin up the DLL to get the Menu Definition
                var menuExtraction = _loader.LoadPlugin(addonId)
                    .Bind(loadedPlugin =>
                    {
                        try
                        {
                            var manifestResult = loadedPlugin.AddonInstance.GetMenuRegistration();
                            return manifestResult;
                        }
                        finally
                        {
                            // CRITICAL: Unload the ALC immediately so the DLL isn't locked in memory
                            loadedPlugin.Context.Unload();
                        }
                    });

                // Get the absolute path to this currently running jcmu.exe
                var coreExePath = Process.GetCurrentProcess().MainModule?.FileName
                                  ?? throw new Exception("Could not determine the executing Core EXE path.");

                // Map the MenuDefinition to the Registry
                return menuExtraction.Bind(menuDef =>
                    _registryManager.RegisterAddon(addonId, menuDef, coreExePath)
                );
            }).ConfigureAwait(false);

        if (result.HasValue)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"\n[SUCCESS] '{addonId}' has been installed and its menus registered.");
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\n[FAILED] '{addonId}' failed during installation or registration:");
            Console.WriteLine($"-> {result.Message}");
            if (result.IsExceptionState) Console.WriteLine($"   {result.Exception!.Message}");
        }

        Console.ResetColor();
        return result;
    }

    /// <summary>
    /// Removes an installed addon from the system and cleans up its registry keys.
    /// Expected format: `jcmu uninstall JCMU.CleanVSBS`
    /// </summary>
    public async Task<Maybe> HandleUninstallAsync(string[] args)
    {
        if (args.Length < 2)
            return Maybe.Fail("Usage: jcmu uninstall <AddonId>");

        var addonId = args[1];

        Console.WriteLine($"\n--- Uninstalling Addon: {addonId} ---");

        // 1. Remove the physical files
        var uninstallResult = await _installer.UninstallAsync(addonId).ConfigureAwait(false);

        // 2. Remove the registry keys (even if the files didn't exist, we still want to clean the registry)
        var registryResult = _registryManager.UnregisterAddon(addonId);

        // Merge the states. If either failed, report the failure.
        var finalResult = uninstallResult.HasValue && registryResult.HasValue
            ? Maybe.SUCCESS
            : Maybe.Fail($"Uninstall: {uninstallResult.Message} | Registry: {registryResult.Message}");

        if (finalResult.HasValue)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"\n[SUCCESS] '{addonId}' has been completely removed.");
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\n[WARNING] Cleanup completed with errors:");
            Console.WriteLine($"-> {finalResult.Message}");
        }

        Console.ResetColor();
        return finalResult;
    }

    /// <summary>
    /// Lists all currently installed addons found in the ProgramData directory.
    /// Expected format: `jcmu list`
    /// </summary>
    public static Task<Maybe> HandleListAsync()
    {
        // (Implementation remains exactly the same as previously defined)
        return Maybe.TryAsync(() =>
        {
            var pluginsDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "JCMU", "Plugins");

            Console.WriteLine("\n--- Installed JCMU Addons ---");

            if (!Directory.Exists(pluginsDirectory))
            {
                Console.WriteLine("No addons currently installed.");
                return Task.FromResult(Maybe.SUCCESS);
            }

            var directories = Directory.GetDirectories(pluginsDirectory);
            if (directories.Length == 0)
            {
                Console.WriteLine("No addons currently installed.");
                return Task.FromResult(Maybe.SUCCESS);
            }

            foreach (var dir in directories)
            {
                var folderName = Path.GetFileName(dir);
                Console.WriteLine($"- {folderName}");
            }

            Console.WriteLine();
            return Task.FromResult(Maybe.SUCCESS);
        });
    }
}