using System.Reflection;
using JinnDev.JCMU.SDK.Interfaces;
using JinnDev.Utilities.Monad;
using Microsoft.Extensions.Logging;

namespace JinnDev.JCMU.ConsoleBed.Runtime;

/// <summary>
/// Provides a clean tuple for returning both the instantiated addon and its memory sandbox,
/// so the orchestrator can execute the addon and subsequently unload the memory.
/// </summary>
public record LoadedPlugin(IJcmuAddon AddonInstance, PluginLoadContext Context);

/// <summary>
/// Responsible for dynamically loading compiled JCMU Addons into isolated memory spaces
/// and instantiating their primary logic classes.
/// </summary>
public class PluginLoader : IPluginLoader
{
    private readonly ILogger<PluginLoader> _logger;

    public PluginLoader(ILogger<PluginLoader> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Loads an installed addon into an isolated AssemblyLoadContext.
    /// </summary>
    /// <param name="addonId">The unique identifier of the installed addon.</param>
    /// <returns>A monad containing the instantiated addon and its memory context.</returns>
    public Maybe<LoadedPlugin> LoadPlugin(string addonId)
    {
        return Maybe.Try<LoadedPlugin>(() =>
        {
            var pluginPath = GetPluginDllPath(addonId);

            _logger.LogDebug("Spinning up AssemblyLoadContext for {AddonId} at {Path}", addonId, pluginPath);

            var loadContext = new PluginLoadContext(pluginPath);

            // Load the primary assembly into the new isolated context
            var assembly = loadContext.LoadFromAssemblyPath(pluginPath);

            var addonInstance = InstantiateAddon(assembly, addonId);

            return new LoadedPlugin(addonInstance, loadContext);
        });
    }

    /// <summary>
    /// Locates the exact path of the primary .dll file in the ProgramData plugin directory.
    /// </summary>
    private static string GetPluginDllPath(string addonId)
    {
        var pluginsBase = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "JCMU", "Plugins");

        if (!Directory.Exists(pluginsBase))
            throw new DirectoryNotFoundException($"Addon '{addonId}' is not installed (Plugins directory not found).");

        // Search across the 2-tier structure
        var targetDirectories = Directory.GetDirectories(pluginsBase)
            .SelectMany(authorDir => Directory.GetDirectories(authorDir))
            .Where(addonDir => Path.GetFileName(addonDir).Equals(addonId, StringComparison.OrdinalIgnoreCase))
            .ToList();

        // Legacy fallback
        var legacyPath = Path.Combine(pluginsBase, addonId);
        if (Directory.Exists(legacyPath) && !targetDirectories.Contains(legacyPath))
            targetDirectories.Add(legacyPath);

        if (targetDirectories.Count == 0)
            throw new DirectoryNotFoundException($"Addon '{addonId}' is not installed.");

        if (targetDirectories.Count > 1)
            throw new Exception($"Ambiguous execution: Multiple addons found with ID '{addonId}' by different authors. " +
                                $"This indicates a hijacking attempt or an unclean uninstall. Please inspect your Plugins directory.");

        var targetDirectory = targetDirectories[0];

        var dllFiles = Directory.GetFiles(targetDirectory, "*.dll");
        if (dllFiles.Length == 0)
            throw new FileNotFoundException($"No compiled .dll files found in {targetDirectory}.");

        var primaryDll = dllFiles.FirstOrDefault(f => Path.GetFileNameWithoutExtension(f).Equals(addonId, StringComparison.OrdinalIgnoreCase))
                         ?? dllFiles[0];

        return primaryDll;
    }

    /// <summary>
    /// Uses Reflection to find the specific class implementing IJcmuAddon and creates an instance of it.
    /// </summary>
    private static IJcmuAddon InstantiateAddon(Assembly assembly, string addonId)
    {
        try
        {
            // Find any public, non-abstract class that implements IJcmuAddon
            var interfaceType = typeof(IJcmuAddon);
            var addonType = assembly.GetTypes().FirstOrDefault(t =>
                interfaceType.IsAssignableFrom(t) &&
                t is { IsInterface: false, IsAbstract: false });

            if (addonType == null)
            {
                throw new Exception($"The loaded assembly for '{addonId}' does not contain any class implementing {nameof(IJcmuAddon)}.");
            }

            // Instantiate using the parameterless constructor
            var instance = Activator.CreateInstance(addonType) as IJcmuAddon;

            if (instance == null)
            {
                throw new Exception($"Failed to instantiate the class '{addonType.Name}' for addon '{addonId}'. Ensure it has a public, parameterless constructor.");
            }

            return instance;
        }
        catch (ReflectionTypeLoadException ex)
        {
            // Safely extract whatever loader exceptions exist
            var loaderErrors = ex.LoaderExceptions
                .Where(e => e != null)
                .Select(e => e!.Message)
                .Distinct()
                .ToList();

            var errorDetails = loaderErrors.Any()
                ? string.Join("\n  -> ", loaderErrors)
                : ex.Message;

            throw new Exception(
                $"SDK Compatibility Error.\n" +
                $"This addon is likely built for an older/incompatible version of the JCMU SDK and needs to be updated by its author.\n" +
                $"Details: {errorDetails}", ex);
        }
        catch (Exception ex)
        {
            throw new Exception($"Failed to load addon. It might be incompatible with this version of JCMU.\nDetails: {ex.Message}", ex);
        }
    }
}