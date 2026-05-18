using JinnDev.JCMU.CoreTools;
using JinnDev.Utilities.Monad;
using Microsoft.Extensions.Logging;

namespace JinnDev.JCMU.ConsoleBed.Cli;

public class ToolCliDispatcher
{
    private readonly IEnumerable<ICoreTool> _tools;
    private readonly ILogger<ToolCliDispatcher> _logger;

    public ToolCliDispatcher(IEnumerable<ICoreTool> tools, ILogger<ToolCliDispatcher> logger)
    {
        _tools = tools;
        _logger = logger;
    }

    /// <summary>
    /// Executes a core tool. Triggered by Windows Explorer (jcmu.exe tool <ToolId> "%V")
    /// or by a manual developer CLI command.
    /// </summary>
    public async Task<Maybe> HandleToolAsync(string toolId, string targetDirectory)
    {
        var tool = _tools.FirstOrDefault(t => t.ToolId.Equals(toolId, StringComparison.OrdinalIgnoreCase));

        if (tool == null)
            return Maybe.Fail($"Built-in tool '{toolId}' not found.");

        _logger.LogInformation("--- Executing Core Tool: {ToolId} ---", toolId);

        var result = await tool.ExecuteAsync(targetDirectory).ConfigureAwait(false);

        // Pause for errors if running in the visible console
        if (!result.HasValue && !Console.IsOutputRedirected && Console.WindowHeight > 0)
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[TOOL FAILED]: {toolId}");
            Console.WriteLine(result.Message);
            if (result.IsExceptionState) Console.WriteLine(result.Exception!.Message);
            Console.ResetColor();
            Console.WriteLine("\nPress any key to exit...");
            Console.ReadKey(true);
        }

        return result;
    }
}