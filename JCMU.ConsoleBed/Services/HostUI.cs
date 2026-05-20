using JinnDev.JCMU.SDK.Interfaces;
using JinnDev.Utilities.Monad;

namespace JinnDev.JCMU.ConsoleBed.Services;

public class HostUI : IHostUI
{
    public Maybe Write(string text, ConsoleColor? color = null) =>
        ExecuteWithColor(() => Console.Write(text), color);

    public Maybe WriteLine(string text = "", ConsoleColor? color = null) =>
        ExecuteWithColor(() => Console.WriteLine(text), color);

    private static Maybe ExecuteWithColor(Action action, ConsoleColor? color)
    {
        return Maybe.Try(() =>
        {
            if (!color.HasValue)
            {
                action();
                return;
            }

            var previousColor = Console.ForegroundColor;
            Console.ForegroundColor = color.Value;
            try
            {
                action();
            }
            finally
            {
                // Guarantee color is reset even if the string write fails somehow
                Console.ForegroundColor = previousColor;
            }
        });
    }
}