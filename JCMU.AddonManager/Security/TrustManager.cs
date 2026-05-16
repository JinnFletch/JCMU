using JinnDev.Utilities.Monad;
using System.Text.Json;

namespace JinnDev.JCMU.AddonManager.Security;

public class TrustManager : ITrustManager
{
    private readonly string _trustFilePath;

    public TrustManager()
    {
        var baseDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "JCMU");
        _trustFilePath = Path.Combine(baseDir, "trust.json");

        EnsureInitialized();
    }

    public bool IsTrusted(string? author)
    {
        if (string.IsNullOrWhiteSpace(author)) return false;
        var trustedList = LoadTrustList();
        return trustedList.Contains(author, StringComparer.OrdinalIgnoreCase);
    }

    public Maybe Trust(string author)
    {
        return Maybe.Try(() =>
        {
            if (string.IsNullOrWhiteSpace(author)) throw new ArgumentException("Author cannot be empty.");

            var list = LoadTrustList();
            if (!list.Contains(author, StringComparer.OrdinalIgnoreCase))
            {
                list.Add(author);
                SaveTrustList(list);
            }
        });
    }

    public Maybe Untrust(string author)
    {
        return Maybe.Try(() =>
        {
            if (string.IsNullOrWhiteSpace(author)) throw new ArgumentException("Author cannot be empty.");

            var list = LoadTrustList();
            // RemoveAll ensures case-insensitive matching if configured
            list.RemoveAll(a => a.Equals(author, StringComparison.OrdinalIgnoreCase));
            SaveTrustList(list);
        });
    }

    private void EnsureInitialized()
    {
        if (!File.Exists(_trustFilePath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_trustFilePath)!);
            SaveTrustList(new List<string> { "JinnFletch" }); // Default trusted publisher
        }
    }

    private List<string> LoadTrustList()
    {
        if (!File.Exists(_trustFilePath)) return new List<string>();
        var json = File.ReadAllText(_trustFilePath);
        return JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
    }

    private void SaveTrustList(List<string> list)
    {
        var json = JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_trustFilePath, json);
    }
}