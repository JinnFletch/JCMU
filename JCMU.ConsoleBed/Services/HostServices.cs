using JinnDev.JCMU.SDK.Interfaces;
using JinnDev.Utilities.Monad;
using Microsoft.Extensions.Logging;
using System.Runtime.Versioning;

namespace JinnDev.JCMU.ConsoleBed.Services;

/// <summary>
/// The concrete implementation of the SDK's IHostServices.
/// Handed to the Addon during execution to provide safe interaction with the core environment.
/// </summary>
[SupportedOSPlatform("windows")]
public partial class HostServices : IHostServices
{
    public IPluginLogger Logger { get; }
    public IAddonSettings Settings { get; }

    public HostServices(string addonId, ILoggerFactory loggerFactory)
    {
        var coreLogger = loggerFactory.CreateLogger("JCMU.PluginRuntime");
        Logger = new HostLogger(addonId, coreLogger);
        Settings = new AddonSettings(addonId);
    }

    public Task<Maybe<string>> PromptUserAsync(string message)
    {
        return Maybe.TryAsync<string>(() =>
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write($"{message} ");
            Console.ResetColor();

            var input = Console.ReadLine();

            if (string.IsNullOrWhiteSpace(input))
            {
                return Task.FromResult(Maybe.None<string>("User provided empty input."));
            }

            return Task.FromResult(Maybe.Some(input));
        });
    }
}