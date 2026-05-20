using JinnDev.JCMU.ConsoleBed.Execution;
using JinnDev.Utilities.Monad;
using Microsoft.Extensions.Logging;

namespace JinnDev.JCMU.ConsoleBed.Cli;

/// <summary>
/// Instantiates and executes a specific addon against a target directory.
/// Expected format: `jcmu execute <AddonId> [-b] "<TargetDirectory>"`
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
        var targetDirectory = args[2].Trim('"');

        _logger.LogInformation("--- Execution Triggered via Shell ---");

        // The Invoker now returns Maybe<int>
        using var cts = new CancellationTokenSource();

        Console.CancelKeyPress += (sender, e) =>
        {
            e.Cancel = true; // Prevent abrupt OS process termination
            cts.Cancel();    // Signal the Addon's token to cancel
            _logger.LogWarning("Cancellation requested by user (CTRL+C). Attempting graceful shutdown...");
        };

        var result = await _invoker.ExecuteAsync(addonId, targetDirectory, cts.Token).ConfigureAwait(false);

        // Only perform interactive UI pauses if we are actually in a visible console
        if (!Console.IsOutputRedirected && Console.WindowHeight > 0)
        {
            await result.MatchAsync(
                someAsync: async seconds =>
                {
                    if (seconds < 0)
                    {
                        Console.WriteLine("\n[Execution Complete] Press any key to exit...");
                        Console.ReadKey(true);
                    }
                    else if (seconds > 0)
                    {
                        Console.WriteLine();
                        await RunCountdownAsync(seconds, "Closing in {0} seconds...").ConfigureAwait(false);
                    }
                },
                noneAsync: async err =>
                {
                    Console.WriteLine();
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"[EXECUTION FAILED]: {addonId}");

                    Console.WriteLine(err.Message);

                    Console.ResetColor();
                    Console.WriteLine();

                    await RunCountdownAsync(10, "Closing due to failure in {0} seconds...").ConfigureAwait(false);
                }
            ).ConfigureAwait(false);
        }

        // Downgrade the result to a parameterless Maybe to satisfy the CLI router
        return result.HasValue ? Maybe.SUCCESS : Maybe.PropagateFailure(result);
    }

    private static async Task RunCountdownAsync(int seconds, string messageTemplate)
    {
        for (int i = seconds; i > 0; i--)
        {
            // \r returns the cursor to the start of the line for a clean overwriting effect
            Console.Write($"\r{string.Format(messageTemplate, i)}   ");
            await Task.Delay(1000).ConfigureAwait(false);
        }
        Console.WriteLine();
    }
}