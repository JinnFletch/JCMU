using System.Runtime.Versioning;

namespace JinnDev.JCMU.HiddenBed;

[SupportedOSPlatform("windows")]
public class Program
{
    // It takes the arguments from Windows and instantly forwards them to your Main app!
    public static async Task<int> Main(string[] args)
    {
        return await ConsoleBed.Program.Main(args).ConfigureAwait(false);
    }
}