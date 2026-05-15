using JinnDev.JCMU.SDK.Interfaces;
using JinnDev.Utilities.Monad;
using Microsoft.Extensions.Logging;

namespace JinnDev.JCMU.ConsoleBed.Services;

/// <summary>
/// The concrete implementation of the SDK's IHostServices.
/// Handed to the Addon during execution to provide safe interaction with the core environment.
/// </summary>
public class HostServices : IHostServices
{
    public IPluginLogger Logger { get; }
    public IProcessRunner CLI { get; }

    public HostServices(string addonId, ILoggerFactory loggerFactory)
    {
        // We create a generic Microsoft ILogger for the "PluginRuntime" category,
        // but the HostLogger wraps it to prefix everything with the specific addonId.
        var coreLogger = loggerFactory.CreateLogger("JCMU.PluginRuntime");

        Logger = new HostLogger(addonId, coreLogger);
        CLI = new HostProcessRunner();
    }

    public Task<Maybe<string>> PromptUserAsync(string message)
    {
        return Maybe.TryAsync<string>(() =>
        {
            Console.WriteLine();

            // Use Cyan to visually distinguish host prompts from standard logs
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write($"{message} ");
            Console.ResetColor();

            var input = Console.ReadLine();

            if (string.IsNullOrWhiteSpace(input))
            {
                // If the user just hits enter, we treat it as an intentional cancellation/empty response,
                // which flows through the pipeline as a None state gracefully.
                return Task.FromResult(Maybe.None<string>("User provided empty input."));
            }

            return Task.FromResult(Maybe.Some(input));
        });
    }
}