using JinnDev.JCMU.AddonManager.Builders;
using JinnDev.JCMU.AddonManager.Installers;
using JinnDev.JCMU.AddonManager.Interfaces;
using JinnDev.JCMU.AddonManager.Models;
using JinnDev.JCMU.AddonManager.Security;
using JinnDev.JCMU.AddonManager.Sources;
using JinnDev.JCMU.ConsoleBed.Cli;
using JinnDev.JCMU.ConsoleBed.Execution;
using JinnDev.JCMU.ConsoleBed.Registry;
using JinnDev.JCMU.ConsoleBed.Runtime;
using JinnDev.JCMU.CoreTools;
using JinnDev.Utilities.CommandLine;
using JinnDev.Utilities.Monad;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using System.Runtime.Versioning;

namespace JinnDev.JCMU.ConsoleBed;

[SupportedOSPlatform("windows")]
public class Program
{
    // Caches to hold numbered list outputs
    private static Dictionary<int, AddonSearchResult>? _lastSearchResults;
    private static IReadOnlyList<string>? _lastListResults;
    private static ServiceProvider _serviceProvider = null!;

    public static async Task<int> Main(string[] args)
    {
        var logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "JCMU", "Logs", "jcmu-core-.txt");

        Log.Logger = new LoggerConfiguration()
                    .MinimumLevel.Information()
                    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning) // Keep MS noise out of logs
                    .WriteTo.Console(
                        // {Message:lj} prints the exact string with no quotes. No timestamps or [INF] tags!
                        outputTemplate: "{Message:lj}{NewLine}{Exception}")
                    .WriteTo.File(logPath,
                        // Keep the file logs highly detailed for troubleshooting
                        outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}",
                        rollingInterval: RollingInterval.Day,
                        retainedFileCountLimit: 7)
                    .CreateLogger();

        try
        {
            var services = new ServiceCollection();
            ConfigureServices(services);
            _serviceProvider = services.BuildServiceProvider();
            await using var _ = _serviceProvider.ConfigureAwait(false);

            var storeDispatcher = _serviceProvider.GetRequiredService<StoreCliDispatcher>();
            var executionDispatcher = _serviceProvider.GetRequiredService<ExecutionCliDispatcher>();
            var coreRegistrar = _serviceProvider.GetRequiredService<CoreRegistrar>();

            // 1. Detect if we are entering the UI/Interactive mode
            bool interactiveMode = args.Length > 0 && args[0].Equals("-i", StringComparison.OrdinalIgnoreCase);
            bool isManualLaunch = args.Length == 0;

            // 2. Always print help at the very top if a UI is being shown
            if (interactiveMode || isManualLaunch)
            {
                PrintHelp();
            }

            string[] commandArgs = args;
            if (interactiveMode)
            {
                commandArgs = args.Skip(1).ToArray(); // Strip -i for the router
            }

            // 3. Execute initial command if provided
            if (commandArgs.Length > 0)
            {
                var result = await RouteCommandAsync(commandArgs, storeDispatcher, executionDispatcher, coreRegistrar).ConfigureAwait(false);

                // Exit immediately if this wasn't an interactive session (e.g. Shell Execute)
                if (!interactiveMode)
                {
                    return result.HasValue ? 0 : 1;
                }
            }

            // 4. REPL Loop
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

                var inputArgs = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                await RouteCommandAsync(inputArgs, storeDispatcher, executionDispatcher, coreRegistrar).ConfigureAwait(false);
            }

            return 0;
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "JCMU Core terminated unexpectedly");
            return 1;
        }
        finally
        {
            await Log.CloseAndFlushAsync().ConfigureAwait(false);
        }
    }

    private static async Task<Maybe> RouteCommandAsync(
        string[] args,
        StoreCliDispatcher storeDispatcher,
        ExecutionCliDispatcher executionDispatcher,
        CoreRegistrar coreRegistrar)
    {
        var verb = args[0].ToLowerInvariant();

        // 1. Capture the memory state for this specific execution turn
        var searchCache = _lastSearchResults;
        var listCache = _lastListResults;

        // 2. Immediately clear the global state. 
        // Only `search` or `list` will refill these for the NEXT turn.
        _lastSearchResults = null;
        _lastListResults = null;

        var toolDispatcher = _serviceProvider.GetRequiredService<ToolCliDispatcher>();

        var executionTask = verb switch
        {
            "search" => storeDispatcher.HandleSearchAsync(args, results => _lastSearchResults = results),
            "install" => storeDispatcher.HandleInstallAsync(args, searchCache),
            "list" => StoreCliDispatcher.HandleListAsync(results => _lastListResults = results),
            "update" => storeDispatcher.HandleUpdateAsync(args, listCache),
            "uninstall" => storeDispatcher.HandleUninstallAsync(args, listCache),
            "execute" => executionDispatcher.HandleExecuteAsync(args),
            "init" => Task.FromResult(InitializePlatform(coreRegistrar)),
            "teardown" => Task.FromResult(TeardownPlatform(coreRegistrar)),
            "trust" => storeDispatcher.HandleTrustAsync(args),
            "untrust" => storeDispatcher.HandleUntrustAsync(args),
            "tool" => args.Length > 2
                      ? toolDispatcher.HandleToolAsync(args[1], args[2].Trim('"'))
                      : Task.FromResult(Maybe.Fail("Usage: jcmu tool <ToolId> <TargetDirectory>")),
            "dev" => RouteDevCommandAsync(args, toolDispatcher),
            _ => Task.FromResult(Maybe.Fail($"Unknown command: {verb}"))
        };

        var result = await executionTask.ConfigureAwait(false);

        if (!result.HasValue && verb != "execute")
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[ERROR] {result.Message}");
            Console.ResetColor();
        }

        return result;
    }

    private static Task<Maybe> RouteDevCommandAsync(string[] args, ToolCliDispatcher toolDispatcher)
    {
        if (args.Length < 2)
            return Task.FromResult(Maybe.Fail("Usage: jcmu dev link [path] OR jcmu dev unlink <AddonId>"));

        return args[1].ToLowerInvariant() switch
        {
            "link" => toolDispatcher.HandleToolAsync("Core.DevLink", args.Length > 2 ? string.Join(" ", args.Skip(2)) : Environment.CurrentDirectory),

            "unlink" => args.Length > 2
                        ? toolDispatcher.HandleToolAsync("Core.DevUnlink", args[2])
                        : Task.FromResult(Maybe.Fail("Usage: jcmu dev unlink <AddonId>")),

            _ => Task.FromResult(Maybe.Fail($"Unknown dev command: {args[1]}"))
        };
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
        services.AddLogging(builder =>
        {
            builder.AddSerilog(dispose: true);
        });

        services.AddSingleton<ITrustManager>(x => new TrustManager());
        services.AddTransient<IAddonSource>(x => new GitHubAddonSource());
        services.AddTransient<IAddonBuilder>(x => new DotNetAddonBuilder());
        services.AddTransient<IAddonInstaller>(x => new AddonInstaller(x.GetRequiredService<IAddonBuilder>(), x.GetRequiredService<ITrustManager>(), x.GetRequiredService<ILogger<AddonInstaller>>()));
        services.AddTransient<IRegistryManager>(x => new RegistryManager(x.GetRequiredService<ILogger<RegistryManager>>()));
        services.AddTransient<CoreRegistrar>(x => new CoreRegistrar(x.GetRequiredService<IRegistryManager>(), x.GetRequiredService<IEnumerable<ICoreTool>>(), x.GetRequiredService<ILogger<CoreRegistrar>>()));
        services.AddSingleton<IPluginLoader>(x => new PluginLoader(x.GetRequiredService<ILogger<PluginLoader>>()));
        services.AddSingleton<IPluginInvoker>(x => new PluginInvoker(x.GetRequiredService<IPluginLoader>(), x.GetRequiredService<ILoggerFactory>()));
        services.AddTransient<StoreCliDispatcher>(x => new StoreCliDispatcher(x.GetRequiredService<IAddonInstaller>(), x.GetRequiredService<IAddonSource>(), x.GetRequiredService<IRegistryManager>(), x.GetRequiredService<ITrustManager>(), x.GetRequiredService<ILogger<StoreCliDispatcher>>()));
        services.AddTransient<ExecutionCliDispatcher>(x => new ExecutionCliDispatcher(x.GetRequiredService<IPluginInvoker>(), x.GetRequiredService<ILogger<ExecutionCliDispatcher>>()));
        
        services.AddStatelessCommandLineRunner();

        // --- AUTO-REGISTER ALL CORE TOOLS ---
        var coreToolType = typeof(ICoreTool);
        var toolTypes = typeof(ICoreTool).Assembly.GetTypes()
            .Where(t => coreToolType.IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);

        foreach (var type in toolTypes)
        {
            // Multiple registrations of `ICoreTool` creates `IEnumerable<ICoreTool>` for dependency injection
            // Transient means a fresh instance is created every time the tool is invoked
            services.AddTransient(coreToolType, type);
        }

        services.AddTransient<ToolCliDispatcher>(x => new ToolCliDispatcher(x.GetRequiredService<IEnumerable<ICoreTool>>(), x.GetRequiredService<ILogger<ToolCliDispatcher>>()));
    }

    private static void PrintHelp()
    {
        Console.WriteLine("\n==================================================");
        Console.WriteLine("    Jinn Context Menu Utility (JCMU) Core CLI     ");
        Console.WriteLine("==================================================\n");

        Console.WriteLine("Package Management:");
        Console.WriteLine("  search <keyword>              Find new addons on GitHub.");
        Console.WriteLine("  list                          Show all currently installed addons.");
        Console.WriteLine("  install <Id | Number>         Install an addon (e.g., 'install 1' after search).");
        Console.WriteLine("  update <Id | Number>          Update an installed addon to the latest version.");
        Console.WriteLine("  uninstall <Id | Number>       Remove an addon (e.g., 'uninstall 1' after list).");
        Console.WriteLine("  trust <Author>                Allow installation of an author's addons.");
        Console.WriteLine("  untrust <Author>              Revoke installation trust for an author.");
        Console.WriteLine("  dev link [path]               Path to a local csproj to test addon development.");
        Console.WriteLine("  dev unlink <AddonId>          The AddonId to unlink once development is over.");

        Console.WriteLine("\nSystem Configuration:");
        Console.WriteLine("  init                          Registers the 'JinnCM' root menu in Windows.");
        Console.WriteLine("  teardown                      Removes all JCMU hooks from the Windows Shell.");
        Console.WriteLine("  exit | quit                   Close the JCMU console.");

        Console.WriteLine("\nAdvanced / Shell Internal:");
        Console.WriteLine("  execute <Id> <Path>           Triggers addon logic (called by Windows Explorer).");

        Console.WriteLine("\n--------------------------------------------------");
        Console.WriteLine("Tip: Use 'search' or 'list' first, then you can use the index number!");
        Console.WriteLine("--------------------------------------------------\n");
    }
}