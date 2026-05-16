using JinnDev.JCMU.ConsoleBed.Execution;
using JinnDev.Utilities.Monad;
using Microsoft.Extensions.Logging;

namespace JinnDev.JCMU.ConsoleBed.Cli;

/// <summary>
/// Handles the hidden execution commands triggered by the Windows Native Shell.
/// </summary>
public class ExecutionCliDispatcher
{
#pragma warning disable SYSLIB1054 // Use LibraryImport instead of DllImport

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern IntPtr GetConsoleWindow();

#pragma warning restore SYSLIB1054

    private const int SW_HIDE = 0;

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
            var msg = "Usage: jcmu execute <AddonId> [-b] \"<TargetDirectory>\"";
            _logger.LogError(msg);
            return Maybe.Fail(msg);
        }

        var addonId = args[1];

        // Parse the optional background flag and adjust the target path index
        bool runInBackground = args[2].Equals("-b", StringComparison.OrdinalIgnoreCase);
        var targetDirectory = runInBackground ? args[3] : args[2];

        // Hide the console window instantly if requested
        if (runInBackground)
        {
            var handle = GetConsoleWindow();
            if (handle != IntPtr.Zero) ShowWindow(handle, SW_HIDE);
        }

        // The Windows Shell passes arguments in quotes if the path has spaces. 
        targetDirectory = targetDirectory.Trim('"');

        _logger.LogInformation("--- Execution Triggered via Shell ---");

        var result = await _invoker.ExecuteAsync(addonId, targetDirectory).ConfigureAwait(false);

        // We only push a console message on failure if it's NOT a background task,
        // because if it is hidden, no one can read it anyway. The core logger handles writing it to disk.
        if (!result.HasValue && !runInBackground)
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

            // Pause so the user can read the error before the console closes
            Console.WriteLine("Press any key to exit...");
            Console.ReadKey(true);
        }

        return result;
    }
}