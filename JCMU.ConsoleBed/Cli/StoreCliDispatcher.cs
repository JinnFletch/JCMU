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

    public async Task<Maybe> HandleInstallAsync(string[] args, Dictionary<int, AddonSearchResult>? searchCache)
    {
        if (args.Length < 2) return Maybe.Fail("Usage: jcmu install <AddonId|Number>");

        var input = args[1];
        var addonId = input;

        if (int.TryParse(input, out var index))
        {
            if (searchCache == null || searchCache.Count == 0)
                return Maybe.Fail("Numeric installation only works immediately after running a 'search'.");

            if (!searchCache.ContainsKey(index))
                return Maybe.Fail($"Invalid index '{index}'. The last search did not display that number.");

            addonId = searchCache[index].AddonId;
        }

        var version = args.Length > 2 ? args[2] : null;

        Console.WriteLine($"\n--- Installing Addon: {addonId} {(version != null ? $"[{version}]" : "[Latest]")} ---");

        var result = await _installer.InstallAsync(_source, addonId, version)
            .BindAsync(async finalDirectory =>
            {
                Maybe<MenuDefinition> menuExtraction = GetMenuDefinitionIsolated(addonId);

                var coreExePath = Process.GetCurrentProcess().MainModule?.FileName
                                  ?? throw new Exception("Could not determine the executing Core EXE path.");

                return menuExtraction.Bind(menuDef =>
                    _registryManager.RegisterAddon(addonId, menuDef, coreExePath)
                );
            }).ConfigureAwait(false);

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

    public async Task<Maybe> HandleUninstallAsync(string[] args, IReadOnlyList<string>? listCache)
    {
        if (args.Length < 2) return Maybe.Fail("Usage: jcmu uninstall <AddonId|Number>");

        var input = args[1];
        var addonId = input;

        if (int.TryParse(input, out var index))
        {
            if (listCache == null || listCache.Count == 0)
                return Maybe.Fail("Numeric uninstallation only works immediately after running a 'list'.");

            if (index < 1 || index > listCache.Count)
                return Maybe.Fail($"Invalid index '{index}'. The last list only had {listCache.Count} results.");

            addonId = listCache[index - 1];
        }

        Console.WriteLine($"\n--- Uninstalling Addon: {addonId} ---");

        RunGarbageCollection();

        var uninstallResult = await _installer.UninstallAsync(addonId).ConfigureAwait(false);
        var registryResult = _registryManager.UnregisterAddon(addonId);

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

    public static Task<Maybe> HandleListAsync(Action<IReadOnlyList<string>> onResultsFound)
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
                onResultsFound(Array.Empty<string>());
                return Task.FromResult(Maybe.SUCCESS);
            }

            var manifests = Directory.GetFiles(pluginsBase, "manifest.json", SearchOption.AllDirectories);

            if (manifests.Length == 0)
            {
                Console.WriteLine("No addons currently installed.");
                onResultsFound(Array.Empty<string>());
                return Task.FromResult(Maybe.SUCCESS);
            }

            var results = new List<string>();
            foreach (var manifestPath in manifests)
            {
                var addonDir = Path.GetDirectoryName(manifestPath);
                if (addonDir == null) continue;

                var relativeId = Path.GetRelativePath(pluginsBase, addonDir).Replace('\\', '/');
                results.Add(relativeId);
            }

            for (int i = 0; i < results.Count; i++)
            {
                Console.WriteLine($"[{i + 1}] {results[i]}");
            }

            onResultsFound(results);
            Console.WriteLine();
            return Task.FromResult(Maybe.SUCCESS);
        });
    }

    public async Task<Maybe> HandleSearchAsync(string[] args, Action<Dictionary<int, AddonSearchResult>> onResultsFound)
    {
        string? query = null;
        int page = 1;

        // Parse logic: If the LAST argument is a number, treat it as the page.
        if (args.Length > 1)
        {
            if (int.TryParse(args.Last(), out var parsedPage))
            {
                page = parsedPage > 0 ? parsedPage : 1;
                // Join everything else back together as the query
                var queryParts = args.Skip(1).Take(args.Length - 2);
                query = string.Join(" ", queryParts);
            }
            else
            {
                query = string.Join(" ", args.Skip(1));
            }

            if (string.IsNullOrWhiteSpace(query)) query = null;
        }

        Console.WriteLine($"\n--- Searching GitHub{(query != null ? $" for '{query}'" : "")} (Page {page}) ---");

        var result = await _source.SearchAsync(query, page).ConfigureAwait(false);

        return result.Tap(pagedResult =>
        {
            var list = pagedResult.Items;
            var cache = new Dictionary<int, AddonSearchResult>();

            if (list.Count == 0)
            {
                Console.WriteLine("No addons found matching that query.");
                onResultsFound(cache);
                return;
            }

            // Calculate offset (e.g. Page 2 starts at 11)
            int startIndex = (page - 1) * 10 + 1;

            for (int i = 0; i < list.Count; i++)
            {
                var displayNum = startIndex + i;
                cache[displayNum] = list[i]; // Store in dict using the exact display number

                Console.WriteLine($"[{displayNum}] {list[i].AddonId}");
                if (!string.IsNullOrEmpty(list[i].Description))
                    Console.WriteLine($"    {list[i].Description}");
            }

            onResultsFound(cache);

            // Display paging footer if total results exceed 10
            if (pagedResult.TotalCount > 10)
            {
                int endItem = startIndex + list.Count - 1;
                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"{startIndex}-{endItem} of {pagedResult.TotalCount} shown");
                Console.ResetColor();
            }
        }).Bind(x => Maybe.SUCCESS);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private Maybe<MenuDefinition> GetMenuDefinitionIsolated(string addonId)
    {
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

    private void RunGarbageCollection()
    {
        for (int i = 0; i < 2; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
    }
}