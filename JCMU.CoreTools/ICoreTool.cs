using JinnDev.JCMU.AddonManager.Models;
using JinnDev.Utilities.Monad;

namespace JinnDev.JCMU.CoreTools;

/// <summary>
/// Defines a built-in JCMU utility. Unlike third-party addons, Core Tools run 
/// directly within the primary application context and bypass memory isolation, 
/// granting them direct access to internal Dependency Injection services.
/// </summary>
public interface ICoreTool
{
    /// <summary>
    /// The unique system identifier for this tool (e.g., "Core.DevLink").
    /// Used for internal routing.
    /// </summary>
    string ToolId { get; }

    /// <summary>
    /// Defines exactly how this tool should appear in the Windows Right-Click menu.
    /// </summary>
    MenuDefinition Menu { get; }

    /// <summary>
    /// Executes the primary logic of the core tool.
    /// </summary>
    /// <param name="targetDirectory">The absolute path of the directory the user right-clicked on.</param>
    /// <returns>A monad representing the success or failure of the execution.</returns>
    Task<Maybe> ExecuteAsync(string targetDirectory);
}