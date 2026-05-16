using JinnDev.JCMU.AddonManager.Models;
using JinnDev.JCMU.ConsoleBed.Registry;
using JinnDev.Utilities.CommandLine;
using JinnDev.Utilities.Monad;
using Microsoft.Extensions.Logging;
using System.Data.Common;
using System.Diagnostics;
using System.Text.Json;

namespace JinnDev.JCMU.ConsoleBed.Cli;

public class DevCliDispatcher
{
    private readonly IRegistryManager _registryManager;
    private readonly IStatelessRunner _cmdRunner;
    private readonly ILogger<DevCliDispatcher> _logger;

    private static readonly string PluginsBase = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "JCMU", "Plugins");

    public DevCliDispatcher(IRegistryManager registryManager, IStatelessRunner cmdRunner, ILogger<DevCliDispatcher> logger)
    {
        _registryManager = registryManager;
        _cmdRunner = cmdRunner;
        _logger = logger;
    }

    public async Task<Maybe> HandleLinkAsync(string[] args)
    {
        // Default to current directory if they didn't provide a path
        var rawPath = args.Length > 2 ? args[2] : Environment.CurrentDirectory;
        var searchDir = Path.GetFullPath(rawPath);

        if (!Directory.Exists(searchDir))
            return Maybe.Fail($"Directory not found: {searchDir}");

        Console.WriteLine($"\n--- Scanning for compiled Addon in: {searchDir} ---");

        // Auto-detect the compiled output (bin/Debug/...) by finding the newest manifest.json
        var manifestFiles = Directory.GetFiles(searchDir, "manifest.json", SearchOption.AllDirectories)
            .Where(f => f.Contains(@"\bin\", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .ToList();

        if (!manifestFiles.Any())
            return Maybe.Fail("Could not find a compiled 'manifest.json' in any 'bin' directory. Did you build the project in Visual Studio first?");

        var targetManifestPath = manifestFiles.First();
        var targetOutputDirectory = Path.GetDirectoryName(targetManifestPath)!;

        // Parse Manifest
        var json = await File.ReadAllTextAsync(targetManifestPath).ConfigureAwait(false);
        var manifest = JsonSerializer.Deserialize<PluginManifest>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                       ?? throw new Exception("Failed to parse manifest.json.");

        var finalPluginDirectory = Path.Combine(PluginsBase, manifest.AddonId);

        // Cleanup existing installation or old link
        if (Directory.Exists(finalPluginDirectory))
        {
            Directory.Delete(finalPluginDirectory, true);
        }

        Directory.CreateDirectory(PluginsBase);

        // Use your CommandLine Utility to create a Windows Junction!
        var request = CommandBuilder.Create("mklink")
            .WithArgument("/J")
            .WithQuotedArgument(finalPluginDirectory)
            .WithQuotedArgument(targetOutputDirectory)
            .Build();

        return await _cmdRunner.RunBufferedAsync(request)
            .MatchAsync(
                success => success.ExitCode != 0 ? Maybe.SUCCESS : Maybe.Fail($"Failed to create Directory Junction: {success.StandardError}"),
                none => Maybe.Fail($"Failed to create Directory Junction: {none.Message}"))
            .BindAsync(res =>
            {
                // Register Registry Keys
                var coreExePath = Process.GetCurrentProcess().MainModule?.FileName
                                  ?? throw new Exception("Could not determine the executing Core EXE path.");

                var registryResult = _registryManager.RegisterAddon(manifest.AddonId, manifest.Menu, coreExePath);

                if (registryResult.HasValue)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"\n[SUCCESS] '{manifest.AddonId}' is now Dev-Linked!");
                    Console.WriteLine($"Junction: {finalPluginDirectory} -> {targetOutputDirectory}");
                    Console.WriteLine("Any rebuilds in Visual Studio will be instantly available in the right-click menu.");
                }

                Console.ResetColor();
                return Task.FromResult(registryResult);
            }).ConfigureAwait(false);
    }

    public Task<Maybe> HandleUnlinkAsync(string[] args)
    {
        if (args.Length < 3) return Task.FromResult(Maybe.Fail("Usage: jcmu dev unlink <AddonId>"));
        var addonId = args[2];

        Console.WriteLine($"\n--- Unlinking Dev Addon: {addonId} ---");

        var targetDirectory = Path.Combine(PluginsBase, addonId);

        if (!Directory.Exists(targetDirectory))
            return Task.FromResult(Maybe.Fail($"No addon found with ID '{addonId}' at {targetDirectory}."));

        // Unregister from Windows Explorer
        _registryManager.UnregisterAddon(addonId);

        // Note: In .NET, Directory.Delete on a Symlink/Junction deletes ONLY the link, not the contents!
        Directory.Delete(targetDirectory, recursive: false);

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"\n[SUCCESS] Dev-Link for '{addonId}' has been removed.");
        Console.ResetColor();

        return Task.FromResult(Maybe.SUCCESS);
    }
}