using JinnDev.JCMU.AddonManager.Interfaces;
using JinnDev.JCMU.AddonManager.Models;
using JinnDev.JCMU.ConsoleBed.Registry;
using JinnDev.JCMU.ConsoleBed.Runtime;
using JinnDev.JCMU.SDK.Models;
using JinnDev.Utilities.Monad;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Runtime.CompilerServices;

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
    public async Task<Maybe> HandleInstallAsync(string[] args, IReadOnlyList<AddonSearchResult> searchCache)
    {
        if (args.Length < 2) return Maybe.Fail("Usage: jcmu install <AddonId|Number>");

        var input = args[1];
        var addonId = input;

        // Check if user provided a number from the last search
        if (int.TryParse(input, out var index))
        {
            if (searchCache == null || index < 1 || index > searchCache.Count)
                return Maybe.Fail($"Invalid index '{index}'. Please run 'search' first.");

            addonId = searchCache[index - 1].AddonId;
        }

        var version = args.Length > 2 ? args[2] : null;

        Console.WriteLine($"\n--- Installing Addon: {addonId} {(version != null ? $"[{version}]" : "[Latest]")} ---");

        var result = await _installer.InstallAsync(_source, addonId, version)
            .BindAsync(async finalDirectory =>
            {
                Maybe<MenuDefinition> menuExtraction = GetMenuDefinitionIsolated(addonId);

                // Get the absolute path to this currently running jcmu.exe
                var coreExePath = Process.GetCurrentProcess().MainModule?.FileName
                                  ?? throw new Exception("Could not determine the executing Core EXE path.");

                // Map the MenuDefinition to the Registry
                return menuExtraction.Bind(menuDef =>
                    _registryManager.RegisterAddon(addonId, menuDef, coreExePath)
                );
            }).ConfigureAwait(false);

        // Kick the GC after install just to be clean
        RunGarbageCollection();

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

        // Run the collector BEFORE we try to delete
        RunGarbageCollection();

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
        return Maybe.TryAsync(() =>
        {
            var pluginsBase = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "JCMU", "Plugins");

            Console.WriteLine("\n--- Installed JCMU Addons ---");

            if (!Directory.Exists(pluginsBase))
            {
                Console.WriteLine("No addons currently installed.");
                return Task.FromResult(Maybe.SUCCESS);
            }

            var manifests = Directory.GetFiles(pluginsBase, "manifest.json", SearchOption.AllDirectories);

            if (manifests.Length == 0)
            {
                Console.WriteLine("No addons currently installed.");
                return Task.FromResult(Maybe.SUCCESS);
            }

            foreach (var manifestPath in manifests)
            {
                var addonDir = Path.GetDirectoryName(manifestPath);
                if (addonDir == null) continue;

                // Get the ID by making the path relative to the Plugins folder
                // e.g. "C:\...\Plugins\JinnFletch\GitInit" -> "JinnFletch\GitInit"
                var relativeId = Path.GetRelativePath(pluginsBase, addonDir).Replace('\\', '/');
                Console.WriteLine($"- {relativeId}");
            }

            Console.WriteLine();
            return Task.FromResult(Maybe.SUCCESS);
        });
    }

    public async Task<Maybe> HandleSearchAsync(string[] args, Action<IReadOnlyList<AddonSearchResult>> onResultsFound)
    {
        if (args.Length < 2) return Maybe.Fail("Usage: jcmu search <Keyword>");
        var query = args[1];

        Console.WriteLine($"\n--- Searching GitHub for '{query}' ---");

        var result = await _source.SearchAsync(query).ConfigureAwait(false);

        return result.Tap(list =>
        {
            if (list.Count == 0)
            {
                Console.WriteLine("No addons found matching that query.");
                return;
            }

            onResultsFound(list); // Store in the cache

            for (int i = 0; i < list.Count; i++)
            {
                Console.WriteLine($"[{i + 1}] {list[i].AddonId}");
                if (!string.IsNullOrEmpty(list[i].Description))
                    Console.WriteLine($"    {list[i].Description}");
            }
        }).Bind(x => Maybe.SUCCESS);
    }



    // This method is NOT async and is marked to prevent the JIT from "holding on" to the variables for debugging.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private Maybe<MenuDefinition> GetMenuDefinitionIsolated(string addonId)
    {
        // Temporarily spin up the DLL to get the Menu Definition
        return _loader.LoadPlugin(addonId)
            .Bind(loadedPlugin =>
            {
                try
                {
                    return loadedPlugin.AddonInstance.GetMenuRegistration();
                }
                finally
                {
                    loadedPlugin.Context.Unload();
                }
            });
    }

    // [NEW HELPER] The "Nuclear" GC Cleanup
    private void RunGarbageCollection()
    {
        for (int i = 0; i < 2; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
    }
}