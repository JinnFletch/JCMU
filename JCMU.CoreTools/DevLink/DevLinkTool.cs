using JinnDev.JCMU.AddonManager.Interfaces;
using JinnDev.JCMU.AddonManager.Models;
using JinnDev.Utilities.CommandLine;
using JinnDev.Utilities.Monad;
using System.Diagnostics;
using System.Text.Json;

namespace JinnDev.JCMU.CoreTools.DevLink;

public class DevLinkTool : ICoreTool
{
    private readonly IRegistryManager _registryManager;
    private readonly IStatelessRunner _cmdRunner;

    private static readonly string PluginsBase = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "JCMU", "Plugins");

    public string ToolId => "Core.DevLink";

    public MenuDefinition Menu => new MenuDefinition
    {
        MenuItemName = "Install Addon Locally (Dev Link)",
        IconPath = "imageres.dll,-163", // Windows Installation Box
        Ordinal = 20,
        RunInBackground = false // We want the console to appear so they see success/errors
    };

    public DevLinkTool(IRegistryManager registryManager, IStatelessRunner cmdRunner)
    {
        _registryManager = registryManager;
        _cmdRunner = cmdRunner;
    }

    public Task<Maybe> ExecuteAsync(string targetDirectory)
    {
        return Maybe.Try(() =>
        {
            var searchDir = Path.GetFullPath(targetDirectory.Trim('"'));
            return Directory.Exists(searchDir)
                ? Maybe<string>.Some(searchDir)
                : Maybe<string>.None($"Directory not found: {searchDir}");
        })
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
        .BindAsync(async targetManifestPath =>
        {
            var outputDir = Path.GetDirectoryName(targetManifestPath)!;
            var json = await File.ReadAllTextAsync(targetManifestPath).ConfigureAwait(false);
            var manifest = JsonSerializer.Deserialize<PluginManifest>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            return manifest != null
                ? Maybe<(PluginManifest Manifest, string OutputDir)>.Some((manifest, outputDir))
                : Maybe<(PluginManifest Manifest, string OutputDir)>.None("Failed to parse manifest.json.");
        })
        .BindAsync(ctx =>
        {
            // Establish the two-tier structure for Dev-Links
            var finalPluginDirectory = Path.Combine(PluginsBase, ctx.Manifest.Author, ctx.Manifest.AddonId);
            if (Directory.Exists(finalPluginDirectory))
                Directory.Delete(finalPluginDirectory, true); // True is safe here; it only deletes the junction, not the target

            Directory.CreateDirectory(Path.Combine(PluginsBase, ctx.Manifest.Author));
            return Maybe<(PluginManifest Manifest, string OutputDir)>.Some(ctx);
        })
        .BindAsync(ctx =>
        {
            var finalPluginDirectory = Path.Combine(PluginsBase, ctx.Manifest.Author, ctx.Manifest.AddonId);
            var request = CommandBuilder.Create("mklink")
                .WithArgument("/J")
                .WithQuotedArgument(finalPluginDirectory)
                .WithQuotedArgument(ctx.OutputDir)
                .Build();

            return _cmdRunner.RunBufferedAsync(request)
                .BindAsync(cmd => cmd.ExitCode == 0
                    ? Maybe.SUCCESS
                    : Maybe.Fail($"Failed to create Directory Junction: {cmd.StandardError}\n{cmd.StandardOutput}"))
                .WithValueAsync(ctx);
        })
        .BindAsync(ctx =>
        {
            var coreExePath = Process.GetCurrentProcess().MainModule?.FileName;
            if (coreExePath == null)
                return Maybe<(PluginManifest Manifest, string OutputDir)>.None("Could not determine the executing Core EXE path.");

            return _registryManager.RegisterAddon(ctx.Manifest.AddonId, ctx.Manifest.Menu, coreExePath)
                .WithValue(ctx);
        })
        .TapAsync(ctx =>
        {
            var finalPluginDirectory = Path.Combine(PluginsBase, ctx.Manifest.Author, ctx.Manifest.AddonId);
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"\n[SUCCESS] '{ctx.Manifest.AddonId}' is now Dev-Linked!");
            Console.WriteLine($"Junction: {finalPluginDirectory} -> {ctx.OutputDir}");
            Console.WriteLine("Any rebuilds in Visual Studio will be instantly available in the right-click menu.");
            Console.ResetColor();
        })
        .BindAsync(_ => Maybe.SUCCESS);
    }
}
