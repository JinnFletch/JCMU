using JinnDev.Utilities.Monad;

namespace JinnDev.JCMU.ConsoleBed.Execution
{
    public interface IPluginInvoker
    {
        Task<Maybe> ExecuteAsync(string addonId, string targetDirectory);
    }
}