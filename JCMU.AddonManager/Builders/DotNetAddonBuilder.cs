using JinnDev.JCMU.AddonManager.Interfaces;
using JinnDev.JCMU.AddonManager.Utilities;
using JinnDev.Utilities.Monad;

namespace JinnDev.JCMU.AddonManager.Builders;

/// <summary>
/// Compiles raw JCMU Addon source code into deployable binaries using the local .NET SDK.
/// </summary>
public class DotNetAddonBuilder : IAddonBuilder
{
    public async Task<Maybe> BuildPluginAsync(string sourceDirectory, string publishDirectory)
    {
        if (!Directory.Exists(sourceDirectory))
            return Maybe.Fail($"Source directory does not exist: {sourceDirectory}");

        return await LocateProjectFile(sourceDirectory)
            .BindAsync(csProjFile =>
            {
                var arguments = new[] { "publish", csProjFile, "-c", "Release", "-o", publishDirectory };
                return CliRunner.RunAsync("dotnet", arguments);
            })
            .BindAsync(cliOutput => VerifyPublishDirectory(publishDirectory))
            .BindAsync(_ => Maybe.SUCCESS)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Recursively searches the source directory for a single .csproj file.
    /// </summary>
    private static Maybe<string> LocateProjectFile(string rootDirectory)
    {
        var projectFiles = Directory.GetFiles(rootDirectory, "*.csproj", SearchOption.AllDirectories);

        if (projectFiles.Length == 0)
            return Maybe.None<string>("No .csproj file found in the repository.");

        if (projectFiles.Length > 1)
        {
            var preferred = projectFiles.FirstOrDefault(f => 
                f.Contains("Addon", StringComparison.OrdinalIgnoreCase) || 
                f.Contains("Plugin", StringComparison.OrdinalIgnoreCase));

            if (preferred != null) 
                return preferred;
            
            return Maybe.None<string>($"Multiple .csproj files found. Unable to determine the primary project: {string.Join(", ", projectFiles)}");
        }

        return projectFiles[0];
    }

    /// <summary>
    /// Verifies the dotnet publish command actually yielded the expected compiled output.
    /// </summary>
    private static Maybe<string> VerifyPublishDirectory(string publishDirectory)
    {
        if (!Directory.Exists(publishDirectory) || !Directory.EnumerateFiles(publishDirectory, "*.dll").Any())
            return Maybe.None<string>("Compilation succeeded, but no DLLs were found in the output directory.");

        return publishDirectory;
    }
}