using JinnDev.JCMU.ConsoleBed.Runtime;
using JinnDev.JCMU.ConsoleBed.Services;
using JinnDev.JCMU.SDK.Models;
using JinnDev.Utilities.Monad;
using Microsoft.Extensions.Logging;

namespace JinnDev.JCMU.ConsoleBed.Execution;

/// <summary>
/// Orchestrates the lifecycle of an Addon execution: Loading, Executing, and Unloading.
/// </summary>
public class PluginInvoker : IPluginInvoker
{
    private readonly IPluginLoader _loader;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<PluginInvoker> _logger;

    public PluginInvoker(IPluginLoader loader, ILoggerFactory loggerFactory)
    {
        _loader = loader;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<PluginInvoker>();
    }

    /// <summary>
    /// Executes the specified addon against the target directory.
    /// </summary>
    /// <param name="addonId">The unique identifier of the installed addon.</param>
    /// <param name="targetDirectory">The absolute path the user right-clicked on.</param>
    /// <returns>A monad representing the success or failure of the execution.</returns>
    public async Task<Maybe<int>> ExecuteAsync(string addonId, string targetDirectory)
    {
        if (string.IsNullOrWhiteSpace(targetDirectory) || !Directory.Exists(targetDirectory))
            return Maybe.None<int>($"Target directory does not exist or is invalid: {targetDirectory}");

        _logger.LogInformation("Invoking Addon '{AddonId}' on target: {TargetDirectory}", addonId, targetDirectory);

        return await _loader.LoadPlugin(addonId)
            .BindAsync(async loadedPlugin =>
            {
                // We use a Try/Finally block inside the BindAsync specifically to guarantee 
                // that the AssemblyLoadContext is unloaded, even if the Addon throws an unhandled exception.
                try
                {
                    return await ExecuteInternalAsync(addonId, targetDirectory, loadedPlugin).ConfigureAwait(false);
                }
                finally
                {
                    _logger.LogDebug("Unloading AssemblyLoadContext for '{AddonId}'.", addonId);
                    loadedPlugin.Context.Unload();
                }
            }).ConfigureAwait(false);
    }

    /// <summary>
    /// The protected inner execution boundary.
    /// </summary>
    private async Task<Maybe<int>> ExecuteInternalAsync(string addonId, string targetDirectory, LoadedPlugin loadedPlugin)
    {
        // 1. Build the toolbelt for the plugin
        var hostServices = new HostServices(addonId, _loggerFactory);

        // 2. Build the snapshot of the world
        var context = new ActionContext { TargetDirectory = targetDirectory, HostServices = hostServices };

        // 3. Double-wrap the execution. 
        // The SDK demands a Task<Maybe>, but we still wrap it in TryAsync just in case 
        // the Addon developer wrote logic that throws before they returned their Monad.
        var executionResult = await Maybe.TryAsync(async () =>
            await loadedPlugin.AddonInstance.ExecuteAsync(context).ConfigureAwait(false)
        ).ConfigureAwait(false);

        if (executionResult.HasValue)
        {
            _logger.LogInformation("Addon '{AddonId}' execution completed successfully.", addonId);
        }
        else
        {
            _logger.LogError(executionResult.Exception, "Addon '{AddonId}' execution failed: {Message}", addonId, executionResult.Message);
        }

        return executionResult;
    }
}