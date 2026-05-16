using JinnDev.Utilities.Monad;

namespace JinnDev.JCMU.ConsoleBed.Runtime
{
    public interface IPluginLoader
    {
        Maybe<LoadedPlugin> LoadPlugin(string addonId);
    }
}