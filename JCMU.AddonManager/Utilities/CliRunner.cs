using System.Diagnostics;
using System.Text;
using JinnDev.Utilities.Monad;

namespace JinnDev.JCMU.AddonManager.Utilities;

/// <summary>
/// A monadic utility for securely executing command-line processes (like git.exe or dotnet.exe),
/// capturing standard output and standard error without throwing exceptions.
/// </summary>
internal static class CliRunner
{
    /// <summary>
    /// Executes a command-line process asynchronously.
    /// </summary>
    /// <param name="fileName">The executable to run (e.g., "git" or "dotnet").</param>
    /// <param name="arguments">The arguments to pass to the executable.</param>
    /// <param name="workingDirectory">The directory to execute the command in.</param>
    /// <returns>A monad containing the standard output on success, or the error output on failure.</returns>
    public static async Task<Maybe<string>> RunAsync(string fileName, IEnumerable<string> arguments, string workingDirectory = "")
    {
        return await Maybe.TryAsync<string>(async () =>
        {
            var processInfo = new ProcessStartInfo
            {
                FileName = fileName,
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            foreach (var arg in arguments)
            {
                processInfo.ArgumentList.Add(arg);
            }

            using var process = new Process();
            process.StartInfo = processInfo;

            var outputBuilder = new StringBuilder();
            var errorBuilder = new StringBuilder();

            process.OutputDataReceived += (_, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                {
                    outputBuilder.AppendLine(e.Data);
                }
            };

            process.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                {
                    errorBuilder.AppendLine(e.Data);
                }
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            // Wait for the process to complete gracefully
            await process.WaitForExitAsync().ConfigureAwait(false);

            var output = outputBuilder.ToString().Trim();
            var error = errorBuilder.ToString().Trim();

            if (process.ExitCode == 0)
            {
                return output;
            }

            // If it failed, prefer the error stream, but fallback to output stream if error is empty
            var failureMessage = !string.IsNullOrWhiteSpace(error) ? error : output;
            if (string.IsNullOrWhiteSpace(failureMessage))
            {
                failureMessage = $"Process '{fileName}' exited with code {process.ExitCode} but provided no error output.";
            }

            throw new Exception(failureMessage);
        }).ConfigureAwait(false);
    }
}