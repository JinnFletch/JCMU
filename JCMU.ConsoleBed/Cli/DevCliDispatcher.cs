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

    public Task<Maybe> HandleLinkAsync(string[] args)
    {
        // 1. Safely Parse Arguments
        return Maybe.Try(() =>
        {
            var rawPath = args.Length > 2 ? string.Join(" ", args.Skip(2)) : Environment.CurrentDirectory;
            return Maybe.Some(Path.GetFullPath(rawPath.Trim('"')));
        })

        // 2. Validate Directory
        .Bind(searchDir => Directory.Exists(searchDir)
            ? Maybe<string>.Some(searchDir)
            : Maybe<string>.None($"Directory not found: {searchDir}"))

        // 3. Locate the Manifest
        .Bind(searchDir =>
        {
            Console.WriteLine($"\n--- Scanning for compiled Addon in: {searchDir} ---");

            var manifestFiles = Directory.GetFiles(searchDir, "manifest.json", SearchOption.AllDirectories)
                .Where(f => f.Contains(@"\bin\", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .ToList();

            return manifestFiles.Any()
                ? Maybe<string>.Some(manifestFiles.First())
                : Maybe<string>.None(
                    "Could not find a compiled 'manifest.json' in any 'bin' directory.\n" +
                    " -> Did you build the project in Visual Studio first?\n" +
                    " -> Is 'manifest.json' set to 'Copy to Output Directory' in your project properties?");
        })

        // 4. Safely Read and Parse JSON
        .BindAsync(async targetManifestPath =>
        {
            var outputDir = Path.GetDirectoryName(targetManifestPath)!;
            var json = await File.ReadAllTextAsync(targetManifestPath).ConfigureAwait(false);
            var manifest = JsonSerializer.Deserialize<PluginManifest>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            return manifest != null
                ? Maybe.Some<(PluginManifest Manifest, string OutputDir)>((manifest, outputDir))
                : Maybe.None<(PluginManifest Manifest, string OutputDir)>("Failed to parse manifest.json.");
        })

        // 5. Safely Prep Directories
        .BindAsync(ctx =>
        {
            var finalPluginDirectory = Path.Combine(PluginsBase, ctx.Manifest.AddonId);
            if (Directory.Exists(finalPluginDirectory))
                Directory.Delete(finalPluginDirectory, true);

            Directory.CreateDirectory(PluginsBase);
            return Maybe.Some(ctx);
        })

        // 6. Execute mklink and evaluate ExitCode
        .BindAsync(ctx =>
        {
            var finalPluginDirectory = Path.Combine(PluginsBase, ctx.Manifest.AddonId);
            var request = CommandBuilder.Create("mklink")
                .WithArgument("/J")
                .WithQuotedArgument(finalPluginDirectory)
                .WithQuotedArgument(ctx.OutputDir)
                .Build();

            return _cmdRunner.RunBufferedAsync(request)
                // Evaluate ExitCode. If 0, convert to SUCCESS, else FAIL.
                .BindAsync(cmd => cmd.ExitCode == 0
                    ? Maybe.SUCCESS
                    : Maybe.Fail($"Failed to create Directory Junction: {cmd.StandardError}\n{cmd.StandardOutput}"))
                // Re-introduce our context tuple back into the pipeline
                .WithValueAsync(ctx);
        })

        // 7. Safely Register Registry Keys
        .BindAsync(ctx =>
        {
            var coreExePath = Process.GetCurrentProcess().MainModule?.FileName;
            if (coreExePath == null)
                return Maybe.None<(PluginManifest Manifest, string OutputDir)>("Could not determine the executing Core EXE path.");

            return _registryManager.RegisterAddon(ctx.Manifest.AddonId, ctx.Manifest.Menu, coreExePath)
                .WithValue(ctx);
        })

        // 8. Side-Effect: Success UI
        .TapAsync(ctx =>
        {
            var finalPluginDirectory = Path.Combine(PluginsBase, ctx.Manifest.AddonId);
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"\n[SUCCESS] '{ctx.Manifest.AddonId}' is now Dev-Linked!");
            Console.WriteLine($"Junction: {finalPluginDirectory} -> {ctx.OutputDir}");
            Console.WriteLine("Any rebuilds in Visual Studio will be instantly available in the right-click menu.");
            Console.ResetColor();
        })

        // 9. Flatten back to parameterless Maybe for the dispatcher
        .BindAsync(_ => Maybe.SUCCESS);
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