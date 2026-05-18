using JinnDev.JCMU.AddonManager.Models;
using JinnDev.Utilities.Monad;
using System.Diagnostics;

namespace JinnDev.JCMU.CoreTools.Tools;

public class SearchTool : ICoreTool
{
    public string ToolId => "Core.Search";

    public MenuDefinition Menu => new MenuDefinition
    {
        MenuItemName = "Search for Addons...",
        IconPath = "imageres.dll,-177", // Magnifying Glass
        Ordinal = 10,
        RunInBackground = false
    };

    public Task<Maybe> ExecuteAsync(string targetDirectory)
    {
        return Maybe.TryAsync(async () =>
        {
            var coreExePath = Process.GetCurrentProcess().MainModule?.FileName;
            if (coreExePath == null) throw new Exception("Could not locate executing process.");

            // Spawn the interactive REPL in the same console window
            var startInfo = new ProcessStartInfo
            {
                FileName = coreExePath,
                Arguments = "-i search",
                UseShellExecute = false
            };

            using var process = Process.Start(startInfo);
            if (process != null)
            {
                await process.WaitForExitAsync().ConfigureAwait(false);
            }

            return Maybe.SUCCESS;
        });
    }
}