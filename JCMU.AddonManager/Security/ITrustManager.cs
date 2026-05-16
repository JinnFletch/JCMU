using JinnDev.Utilities.Monad;

namespace JinnDev.JCMU.AddonManager.Security;

public interface ITrustManager
{
    bool IsTrusted(string? author);
    Maybe Trust(string author);
    Maybe Untrust(string author);
}
