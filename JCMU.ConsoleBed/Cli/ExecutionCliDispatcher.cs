using JinnDev.JCMU.ConsoleBed.Execution;
using JinnDev.Utilities.Monad;
using Microsoft.Extensions.Logging;

namespace JinnDev.JCMU.ConsoleBed.Cli;

/// <summary>
/// Handles the hidden execution commands triggered by the Windows Native Shell.
/// </summary>
public class ExecutionCliDispatcher
{
    private readonly IPluginInvoker _invoker;
    private readonly ILogger<ExecutionCliDispatcher> _logger;

    public ExecutionCliDispatcher(IPluginInvoker invoker, ILogger<ExecutionCliDispatcher> logger)
    {
        _invoker = invoker;
        _logger = logger;
    }

    /// <summary>
    /// Instantiates and executes a specific addon against a target directory.
    /// Expected format: `jcmu execute <AddonId> [-b] "<TargetDirectory>"`
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
        var targetDirectory = args[2].Trim('"'); // Strip quotes passed by Windows Explorer

        _logger.LogInformation("--- Execution Triggered via Shell ---");

        var result = await _invoker.ExecuteAsync(addonId, targetDirectory).ConfigureAwait(false);

        // If it's running in the visible Console (because it failed or RunInBackground = false)
        // We want to pause so the user can actually read the error before the box closes.
        if (!result.HasValue && !Console.IsOutputRedirected && Console.WindowHeight > 0)
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[EXECUTION FAILED]: {addonId}");
            Console.WriteLine(result.Message);
            if (result.IsExceptionState) Console.WriteLine(result.Exception!.Message);
            Console.ResetColor();
            Console.WriteLine("\nPress any key to exit...");
            Console.ReadKey(true);
        }

        return result;
    }
}