using JinnDev.JCMU.AddonManager.Builders;
using JinnDev.JCMU.AddonManager.Installers;
using JinnDev.JCMU.AddonManager.Interfaces;
using JinnDev.JCMU.AddonManager.Sources;
using JinnDev.JCMU.ConsoleBed.Cli;
using JinnDev.JCMU.ConsoleBed.Execution;
using JinnDev.JCMU.ConsoleBed.Runtime;
using JinnDev.Utilities.Monad;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace JinnDev.JCMU.ConsoleBed;

public class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0)
        {
            PrintHelp();
            return 0;
        }

        var services = new ServiceCollection();
        ConfigureServices(services);
        var provider = services.BuildServiceProvider();
        await using var _ = provider.ConfigureAwait(false);

        var storeDispatcher = provider.GetRequiredService<StoreCliDispatcher>();
        var executionDispatcher = provider.GetRequiredService<ExecutionCliDispatcher>();

        var verb = args[0].ToLowerInvariant();

        // Map the terminal arguments to the appropriate monadic execution path
        var executionTask = verb switch
        {
            "install" => storeDispatcher.HandleInstallAsync(args),
            "uninstall" => storeDispatcher.HandleUninstallAsync(args),
            "list" => storeDispatcher.HandleListAsync(),
            "execute" => executionDispatcher.HandleExecuteAsync(args),
            _ => Task.FromResult(Maybe.Fail($"Unknown command: {verb}"))
        };

        var result = await executionTask.ConfigureAwait(false);

        // Translate the functional Monad state into standard OS exit codes
        return result.Match(
            some: () => 0,
            none: x => 1
        );
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
        services.AddTransient<IAddonSource, GitHubAddonSource>();
        services.AddTransient<IAddonBuilder, DotNetAddonBuilder>();
        services.AddTransient<IAddonInstaller, AddonInstaller>();

        // 3. Core Engine (Phase 3 Execution)
        services.AddSingleton<PluginLoader>();
        services.AddSingleton<PluginInvoker>();

        // 4. CLI Dispatchers
        services.AddTransient<StoreCliDispatcher>();
        services.AddTransient<ExecutionCliDispatcher>();
    }

    private static void PrintHelp()
    {
        Console.WriteLine("==================================================");
        Console.WriteLine("    Jinn Context Menu Utils (JCMU) Core CLI       ");
        Console.WriteLine("==================================================\n");
        Console.WriteLine("Commands:");
        Console.WriteLine("  install <AddonId> [Version]   Downloads and installs an addon from GitHub.");
        Console.WriteLine("  uninstall <AddonId>           Removes an installed addon from the system.");
        Console.WriteLine("  list                          Lists all currently installed addons.");
        Console.WriteLine("  execute <AddonId> <Path>      Executes an addon against a target directory.");
        Console.WriteLine("\nExamples:");
        Console.WriteLine("  jcmu install JCMU.CleanVSBS");
        Console.WriteLine("  jcmu execute JCMU.CleanVSBS \"C:\\MyCode\"");
    }
}