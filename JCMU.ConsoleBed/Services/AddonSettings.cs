using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using JinnDev.JCMU.SDK.Interfaces;
using JinnDev.Utilities.Monad;

namespace JinnDev.JCMU.ConsoleBed.Services;

[SupportedOSPlatform("windows")]
public class AddonSettings : IAddonSettings
{
    private readonly string _configFilePath;
    private readonly SemaphoreSlim _fileLock = new(1, 1);

    // We use a specific entropy to add a slight extra layer of obfuscation
    private readonly byte[] _entropy = Encoding.UTF8.GetBytes("JCMU_Addon_Secret_Salt");

    public AddonSettings(string addonId)
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _configFilePath = Path.Combine(localAppData, "JCMU", "Configs", $"{addonId}.json");
    }

    public Task<Maybe<T>> GetValueAsync<T>(string key)
    {
        return LoadSettingsAsync().BindAsync(settings =>
        {
            if (!settings.Values.TryGetValue(key, out var jsonElement))
                return Task.FromResult(Maybe<T>.None($"Setting '{key}' not found."));

            try
            {
                var value = jsonElement.Deserialize<T>();
                return Task.FromResult(value != null
                    ? Maybe<T>.Some(value)
                    : Maybe<T>.None($"Setting '{key}' was found but deserialized to null."));
            }
            catch (Exception ex)
            {
                return Task.FromResult(Maybe<T>.None($"Failed to deserialize setting '{key}' to type {typeof(T).Name}: {ex.Message}"));
            }
        });
    }

    public Task<Maybe> SetValueAsync<T>(string key, T value)
    {
        return LoadSettingsAsync().BindAsync(settings =>
        {
            var jsonElement = JsonSerializer.SerializeToElement(value);
            settings.Values[key] = jsonElement;
            return SaveSettingsAsync(settings);
        });
    }

    public Task<Maybe<string>> GetSecretAsync(string key)
    {
        return LoadSettingsAsync().BindAsync(settings => Maybe.Try(() =>
        {
            if (!settings.Secrets.TryGetValue(key, out var encryptedBase64))
                return Maybe<string>.None($"Secret '{key}' not found.");

            var encryptedBytes = Convert.FromBase64String(encryptedBase64);

            // DPAPI Decryption: Will fail if a different Windows User tries to decrypt it
            var decryptedBytes = ProtectedData.Unprotect(encryptedBytes, _entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(decryptedBytes);
        }));
    }

    public Task<Maybe> SetSecretAsync(string key, string value)
    {
        return LoadSettingsAsync().BindAsync(settings => Maybe.TryAsync(async () =>
        {
            var plaintextBytes = Encoding.UTF8.GetBytes(value);

            // DPAPI Encryption: Tied directly to the logged-in Windows User Profile
            var encryptedBytes = ProtectedData.Protect(plaintextBytes, _entropy, DataProtectionScope.CurrentUser);
            var encryptedBase64 = Convert.ToBase64String(encryptedBytes);

            settings.Secrets[key] = encryptedBase64;

            return await SaveSettingsAsync(settings).ConfigureAwait(false);
        }));
    }

    // --- Private IO & State Handling ---

    private Task<Maybe<SettingsStore>> LoadSettingsAsync()
    {
        return Maybe.TryAsync(async () =>
        {
            await _fileLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (!File.Exists(_configFilePath))
                    return Maybe<SettingsStore>.Some(new SettingsStore());

                var json = await File.ReadAllTextAsync(_configFilePath).ConfigureAwait(false);
                var store = JsonSerializer.Deserialize<SettingsStore>(json) ?? new SettingsStore();
                return Maybe<SettingsStore>.Some(store);
            }
            finally
            {
                _fileLock.Release();
            }
        });
    }

    private Task<Maybe> SaveSettingsAsync(SettingsStore store)
    {
        return Maybe.TryAsync(async () =>
        {
            await _fileLock.WaitAsync().ConfigureAwait(false);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_configFilePath)!);
                var json = JsonSerializer.Serialize(store, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(_configFilePath, json).ConfigureAwait(false);
                return Maybe.SUCCESS;
            }
            finally
            {
                _fileLock.Release();
            }
        });
    }

    // DTO for JSON Serialization
    private class SettingsStore
    {
        public Dictionary<string, JsonElement> Values { get; set; } = new();
        public Dictionary<string, string> Secrets { get; set; } = new();
    }
}