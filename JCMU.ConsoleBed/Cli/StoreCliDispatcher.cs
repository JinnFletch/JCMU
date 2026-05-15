using JinnDev.JCMU.AddonManager.Interfaces;
using JinnDev.Utilities.Monad;
using Microsoft.Extensions.Logging;

namespace JinnDev.JCMU.ConsoleBed.Cli;

/// <summary>
/// Handles user-facing CLI commands related to Addon package management.
/// </summary>
public class StoreCliDispatcher
{
    private readonly IAddonInstaller _installer;
    private readonly IAddonSource _source;
    private readonly ILogger<StoreCliDispatcher> _logger;

    public StoreCliDispatcher(IAddonInstaller installer, IAddonSource source, ILogger<StoreCliDispatcher> logger)
    {
        _installer = installer;
        _source = source;
        _logger = logger;
    }

    /// <summary>
    /// Executes the installation pipeline for a specific addon.
    /// Expected format: `jcmu install JCMU.CleanVSBS [optionalVersion]`
    /// </summary>
    public async Task<Maybe> HandleInstallAsync(string[] args)
    {
        if (args.Length < 2)
            return Maybe.Fail("Usage: jcmu install <AddonId> [Version]");

        var addonId = args[1];
        var version = args.Length > 2 ? args[2] : null;

        Console.WriteLine($"\n--- Installing Addon: {addonId} {(version != null ? $"[{version}]" : "[Latest]")} ---");

        var result = await _installer.InstallAsync(_source, addonId, version).ConfigureAwait(false);

        if (result.HasValue)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"\n[SUCCESS] '{addonId}' has been installed and is ready to use.");
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\n[FAILED] '{addonId}' failed to install:");
            Console.WriteLine($"-> {result.Message}");
        }
        Console.ResetColor();

        return result;
    }

    /// <summary>
    /// Removes an installed addon from the system.
    /// Expected format: `jcmu uninstall JCMU.CleanVSBS`
    /// </summary>
    public async Task<Maybe> HandleUninstallAsync(string[] args)
    {
        if (args.Length < 2)
            return Maybe.Fail("Usage: jcmu uninstall <AddonId>");

        var addonId = args[1];

        Console.WriteLine($"\n--- Uninstalling Addon: {addonId} ---");

        var result = await _installer.UninstallAsync(addonId).ConfigureAwait(false);

        if (result.HasValue)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"\n[SUCCESS] '{addonId}' has been completely removed.");
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\n[FAILED] '{addonId}' failed to uninstall:");
            Console.WriteLine($"-> {result.Message}");
        }
        Console.ResetColor();

        return result;
    }

    /// <summary>
    /// Lists all currently installed addons found in the ProgramData directory.
    /// Expected format: `jcmu list`
    /// </summary>
    public Task<Maybe> HandleListAsync()
    {
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