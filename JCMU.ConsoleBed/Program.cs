using System.Runtime.Versioning;
using JinnDev.JCMU.AddonManager.Builders;
using JinnDev.JCMU.AddonManager.Installers;
using JinnDev.JCMU.AddonManager.Interfaces;
using JinnDev.JCMU.AddonManager.Sources;
using JinnDev.JCMU.ConsoleBed.Cli;
using JinnDev.JCMU.ConsoleBed.Execution;
using JinnDev.JCMU.ConsoleBed.Registry;
using JinnDev.JCMU.ConsoleBed.Runtime;
using JinnDev.Utilities.Monad;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace JinnDev.JCMU.ConsoleBed;

[SupportedOSPlatform("windows")]
public class Program
{
    public static async Task<int> Main(string[] args)
    {
        var services = new ServiceCollection();
        ConfigureServices(services);
        var provider = services.BuildServiceProvider();
        await using var _ = provider.ConfigureAwait(false);

        var storeDispatcher = provider.GetRequiredService<StoreCliDispatcher>();
        var executionDispatcher = provider.GetRequiredService<ExecutionCliDispatcher>();
        var coreRegistrar = provider.GetRequiredService<CoreRegistrar>();

        // If args were passed (e.g., from Explorer), run once and exit.
        if (args.Length > 0)
        {
            var result = await RouteCommandAsync(args, storeDispatcher, executionDispatcher, coreRegistrar).ConfigureAwait(false);
            return result.HasValue ? 0 : 1;
        }

        // If no args, enter interactive mode
        PrintHelp();
        while (true)
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write("JCMU> ");
            Console.ResetColor();

            var input = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(input)) continue;

            if (input.Equals("exit", StringComparison.OrdinalIgnoreCase) ||
                input.Equals("quit", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            // Split the input into an args array (basic space splitting)
            var inputArgs = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            await RouteCommandAsync(inputArgs, storeDispatcher, executionDispatcher, coreRegistrar).ConfigureAwait(false);
        }

        return 0;
    }

    private static async Task<Maybe> RouteCommandAsync(
        string[] args,
        StoreCliDispatcher storeDispatcher,
        ExecutionCliDispatcher executionDispatcher,
        CoreRegistrar coreRegistrar)
    {
        var verb = args[0].ToLowerInvariant();

        var executionTask = verb switch
        {
            "install" => storeDispatcher.HandleInstallAsync(args),
            "uninstall" => storeDispatcher.HandleUninstallAsync(args),
            "list" => StoreCliDispatcher.HandleListAsync(),
            "execute" => executionDispatcher.HandleExecuteAsync(args),
            "init" => Task.FromResult(InitializePlatform(coreRegistrar)),
            "teardown" => Task.FromResult(TeardownPlatform(coreRegistrar)),
            _ => Task.FromResult(Maybe.Fail($"Unknown command: {verb}"))
        };

        var result = await executionTask.ConfigureAwait(false);

        if (!result.HasValue && verb != "execute")
        {
            // Execute handles its own error printing, but for others we print it here.
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[ERROR] {result.Message}");
            Console.ResetColor();
        }

        return result;
    }

    private static Maybe InitializePlatform(CoreRegistrar registrar)
    {
        var result = registrar.InitializeCore();
        if (result.HasValue)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("\n[SUCCESS] JCMU Core Registry Anchors initialized.");
            Console.WriteLine("You can now install addons, and they will appear in your right-click menu.");
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\n[FAILED] Initialization failed: {result.Message}");
        }
        Console.ResetColor();
        return result;
    }

    private static Maybe TeardownPlatform(CoreRegistrar registrar)
    {
        var result = registrar.TeardownCore();
        if (result.HasValue)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("\n[SUCCESS] JCMU Core Registry Anchors removed.");
            Console.WriteLine("The JCMU menu will no longer appear when you right-click.");
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\n[FAILED] Teardown failed: {result.Message}");
        }
        Console.ResetColor();
        return result;
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        // 1. Logging
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });

        // 2. Addon Manager (Phase 2 Store)
        services.AddTransient<IAddonSource>(x => new GitHubAddonSource());
        services.AddTransient<IAddonBuilder>(x => new DotNetAddonBuilder());
        services.AddTransient<IAddonInstaller>(x => new AddonInstaller(x.GetRequiredService<IAddonBuilder>(), x.GetRequiredService<ILogger<AddonInstaller>>()));

        // 3. Registry Abstractions
        services.AddTransient<IRegistryManager>(x => new RegistryManager(x.GetRequiredService<ILogger<RegistryManager>>()));
        services.AddTransient<CoreRegistrar>(x => new CoreRegistrar(x.GetRequiredService<ILogger<CoreRegistrar>>()));

        // 4. Core Engine (Phase 3 Execution)
        services.AddSingleton<IPluginLoader>(x => new PluginLoader(x.GetRequiredService<ILogger<PluginLoader>>()));
        services.AddSingleton<IPluginInvoker>(x => new PluginInvoker(x.GetRequiredService<IPluginLoader>(), x.GetRequiredService<ILoggerFactory>()));

        // 5. CLI Dispatchers
        services.AddTransient<StoreCliDispatcher>(x => new StoreCliDispatcher(x.GetRequiredService<IAddonInstaller>(), x.GetRequiredService<IAddonSource>(), x.GetRequiredService<IPluginLoader>(), x.GetRequiredService<IRegistryManager>(), x.GetRequiredService<ILogger<StoreCliDispatcher>>()));
        services.AddTransient<ExecutionCliDispatcher>(x => new ExecutionCliDispatcher(x.GetRequiredService<IPluginInvoker>(), x.GetRequiredService<ILogger<ExecutionCliDispatcher>>()));
    }

    private static void PrintHelp()
    {
        Console.WriteLine("==================================================");
        Console.WriteLine("    Jinn Context Menu Utils (JCMU) Core CLI       ");
        Console.WriteLine("==================================================\n");
        Console.WriteLine("System Commands:");
        Console.WriteLine("  init                          Registers the root 'JinnCM' menu in Windows.");
        Console.WriteLine("  teardown                      Removes the JCMU root menus from Windows.");
        Console.WriteLine("\nPackage Commands:");
        Console.WriteLine("  install <AddonId> [Version]   Downloads, compiles, and registers a GitHub addon.");
        Console.WriteLine("  uninstall <AddonId>           Removes an addon and its registry keys.");
        Console.WriteLine("  list                          Lists all currently installed addons.");
        Console.WriteLine("  execute <AddonId> <Path>      (Internal) Executes an addon against a target directory.");
    }
}