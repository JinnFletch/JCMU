namespace JinnDev.JCMU.AddonManager.Models;

public record AddonPagedResult(
    IReadOnlyList<AddonSearchResult> Items,
    int TotalCount
);