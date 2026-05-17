using JinnDev.JCMU.SDK.Interfaces;
using JinnDev.Utilities.Monad;
using Microsoft.Extensions.Logging;

namespace JinnDev.JCMU.ConsoleBed.Services;

/// <summary>
/// A standardized logger provided to addons during execution.
/// Prefixes all logs with the addon's identity to ensure clear origin tracking in the console.
/// </summary>
public class HostLogger : IPluginLogger
{
    private readonly string _addonPrefix;
    private readonly ILogger _logger;

    public HostLogger(string addonId, ILogger logger)
    {
        _addonPrefix = $"[{addonId}]";
        _logger = logger;
    }

    public Maybe LogInfo(string message)
    {
        return Maybe.Try(() =>
        {
            _logger.LogInformation("{Message}", message);
        });
    }

    public Maybe LogWarning(string message)
    {
        return Maybe.Try(() =>
        {
            _logger.LogWarning("{AddonPrefix} {Message}", _addonPrefix, message);
        });
    }

    public Maybe LogError(string message, Exception? ex = null)
    {
        return Maybe.Try(() =>
        {
            if (ex == null)
            {
                _logger.LogError("{AddonPrefix} {Message}", _addonPrefix, message);
            }
            else
            {
                _logger.LogError(ex, "{AddonPrefix} {Message}", _addonPrefix, message);
            }
        });
    }
}