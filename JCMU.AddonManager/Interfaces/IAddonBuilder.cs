using JinnDev.Utilities.Monad;

namespace JinnDev.JCMU.AddonManager.Interfaces;

/// <summary>
/// Responsible for compiling raw C# source code into a secure, ready-to-load JCMU Addon.
/// </summary>
public interface IAddonBuilder
{
    /// <summary>
    /// Locates the .csproj in the source directory and executes 'dotnet publish' in Release mode.
    /// </summary>
    /// <param name="sourceDirectory">The directory containing the downloaded raw source code.</param>
    /// <param name="publishDirectory">The directory where the compiled binaries should be written.</param>
    /// <returns>A monad containing the absolute path to the compiled output directory.</returns>
    Task<Maybe> BuildPluginAsync(string sourceDirectory, string publishDirectory);
}
