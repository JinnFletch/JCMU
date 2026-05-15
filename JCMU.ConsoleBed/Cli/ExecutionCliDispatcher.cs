using JinnDev.JCMU.ConsoleBed.Execution;
using JinnDev.Utilities.Monad;
using Microsoft.Extensions.Logging;

namespace JinnDev.JCMU.ConsoleBed.Cli;

/// <summary>
/// Handles the hidden execution commands triggered by the Windows Native Shell.
/// </summary>
public class ExecutionCliDispatcher
{
    private readonly PluginInvoker _invoker;
    private readonly ILogger<ExecutionCliDispatcher> _logger;

    public ExecutionCliDispatcher(PluginInvoker invoker, ILogger<ExecutionCliDispatcher> logger)
    {
        _invoker = invoker;
        _logger = logger;
    }

    /// <summary>
    /// Instantiates and executes a specific addon against a target directory.
    /// Expected format: `jcmu execute <AddonId> "<TargetDirectory>"`
    /// </summary>
    public async Task<Maybe> HandleExecuteAsync(string[] args)
    {
        if (args.Length < 3)
        {
            var msg = "Usage: jcmu execute <AddonId> \"<TargetDirectory>\"";
            _logger.LogError(msg);
            return Maybe.Fail(msg);
        }

        var addonId = args[1];
        var targetDirectory = args[2];

        // The Windows Shell passes arguments in quotes if the path has spaces. 
        // We strip them to ensure clean Directory.Exists checks down the line.
        targetDirectory = targetDirectory.Trim('"');

        _logger.LogInformation("--- Execution Triggered via Shell ---");

        var result = await _invoker.ExecuteAsync(addonId, targetDirectory).ConfigureAwait(false);

        // We only push a console message on failure here, because if it's headless, 
        // the console will just close instantly on success. If it fails, the Core Program.cs 
        // will keep the window open so the user can read the error.
        if (!result.HasValue)
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[EXECUTION FAILED]: {addonId}");
            Console.WriteLine(result.Message);
            if (result.IsExceptionState)
            {
                Console.WriteLine(result.Exception!.Message);
            }
            Console.ResetColor();
            Console.WriteLine();
        }

        return result;
    }
}