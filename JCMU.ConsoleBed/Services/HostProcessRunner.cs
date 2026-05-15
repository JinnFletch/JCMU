using System.Diagnostics;
using JinnDev.JCMU.SDK.Interfaces;
using JinnDev.Utilities.Monad;

namespace JinnDev.JCMU.ConsoleBed.Services;

/// <summary>
/// Provides secure, deadlock-free execution of command-line instructions for addons.
/// </summary>
public class HostProcessRunner : IProcessRunner
{
    public async Task<Maybe<IReadOnlyList<string>>> RunCommandAsync(string workingDirectory, string command)
    {
        return await ExecuteCommandInternalAsync(workingDirectory, command).ConfigureAwait(false);
    }

    public async Task<Maybe<IReadOnlyDictionary<int, IReadOnlyList<string>>>> RunCommandsAsync(string workingDirectory, IEnumerable<string> commands)
    {
        // Seed the pipeline with an empty dictionary wrapped in a successful Maybe Task
        var initialPipeline = Task.FromResult(Maybe.Some<IReadOnlyDictionary<int, IReadOnlyList<string>>>(
            new Dictionary<int, IReadOnlyList<string>>()));

        // Fold the commands functionally. 
        // If any step returns None, BindAsync automatically short-circuits all subsequent commands.
        return await commands
            .Select((command, index) => new { command, index })
            .Aggregate(initialPipeline, (currentContextTask, item) =>
                currentContextTask.BindAsync(dict =>
                    ExecuteCommandInternalAsync(workingDirectory, item.command)
                        .MapAsync(output =>
                        {
                            var mutableDict = (Dictionary<int, IReadOnlyList<string>>)dict;
                            mutableDict.Add(item.index, output);
                            return (IReadOnlyDictionary<int, IReadOnlyList<string>>)mutableDict;
                        })
                )
            ).ConfigureAwait(false);
    }

    /// <summary>
    /// Executes a single command via cmd.exe, capturing standard output and error asynchronously.
    /// </summary>
    private static async Task<Maybe<IReadOnlyList<string>>> ExecuteCommandInternalAsync(string workingDirectory, string command)
    {
        try
        {
            var processInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c \"{command}\"",
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process();
            process.StartInfo = processInfo;

            var outputLines = new List<string>();
            var errorLines = new List<string>();

            process.OutputDataReceived += (_, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                    outputLines.Add(e.Data);
            };

            process.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                    errorLines.Add(e.Data);
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync().ConfigureAwait(false);

            if (process.ExitCode == 0)
            {
                return Maybe.Some<IReadOnlyList<string>>(outputLines);
            }

            var errorMessage = errorLines.Count > 0
                ? string.Join(Environment.NewLine, errorLines)
                : string.Join(Environment.NewLine, outputLines);

            return Maybe.None<IReadOnlyList<string>>($"Process exited with code {process.ExitCode}. Output: {errorMessage}");
        }
        catch (Exception ex)
        {
            return Maybe.None<IReadOnlyList<string>>(ex, $"An unexpected error occurred while executing command: {command}");
        }
    }
}