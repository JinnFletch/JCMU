using JinnDev.Utilities.Monad;

namespace JinnDev.JCMU.ConsoleBed.Execution
{
    public interface IPluginInvoker
    {
        Task<Maybe<int>> ExecuteAsync(string addonId, string targetDirectory);
    }
}