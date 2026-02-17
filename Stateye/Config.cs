using System.Numerics.Tensors;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Stateye;

/// <summary>
/// Application constants and configuration values.
/// </summary>
public static class AppConstants
{
    /// <summary>Roblox Player Discord application id.</summary>
    public const string PlayerDiscordAppId = "1040069190322749481";

    /// <summary>Roblox Studio Discord application id.</summary>
    public const string StudioDiscordAppId = "1040435667483775047";

    /// <summary>How often to update Discord activity presence status, in milliseconds.</summary>
    public const int FrequencyOfStatusUpdates = 1000;

    /// <summary>Name of the file where program configuration will be read from.</summary>
    public const string ConfigFileName = "stateye.config.json";
}

[JsonSerializable(typeof(Config))]
[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    WriteIndented = true, IndentSize = 2)]
internal sealed partial class ConfigSerializerContext : JsonSerializerContext;

/// <summary>
/// Runtime configuration loaded from the config file.
/// </summary>
public sealed class Config
{
    private const string TokenIsEncryptedMarker = "stateye.net9B76955B-0F03-402C-A94B-140C7C1F4215";

    public string Token { get; set; }
    public bool Website { get; set; }
    public bool Player { get; set; } = true;
    public bool Studio { get; set; }

    /// <summary>
    /// Loads configuration from the config file relative to the working directory.
    /// Falls back to defaults if the file is missing or malformed.
    /// </summary>
    public static async Task<Config> LoadAsync()
    {
        var configPath = Path.Combine(Directory.GetCurrentDirectory(), AppConstants.ConfigFileName);

        if (!File.Exists(configPath))
        {
            Log.Debug("Config file not found");
            return null;
        }

        Log.Debug($"Loaded config from {configPath}");

        await using var fs = new FileStream(configPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None, 4096, FileOptions.Asynchronous);
        var config = await JsonSerializer.DeserializeAsync<Config>(fs, ConfigSerializerContext.Default.Options);

        if (config.Token is string token && token.Length > TokenIsEncryptedMarker.Length)
        {
            if (config.Token.StartsWith(TokenIsEncryptedMarker))
            {
                Log.Debug("Decrypting token from config file");

                // span out the encrypted token and decrypt it
                var encryptedTokenSpan = token[TokenIsEncryptedMarker.Length..];
                var encryptedTokenBytes = Convert.FromBase64String(encryptedTokenSpan);
                var decryptedTokenBytes = ProtectedData.Unprotect(encryptedTokenBytes, null, DataProtectionScope.CurrentUser);
                config.Token = Encoding.ASCII.GetString(decryptedTokenBytes);
            }
            else
            {
                Log.Debug("Token in config file is not encrypted, so we're doing that");

                var tokenBytes = Encoding.ASCII.GetBytes(config.Token);
                var encryptedTokenBytes = ProtectedData.Protect(tokenBytes, null, DataProtectionScope.CurrentUser);
                var encryptedToken = TokenIsEncryptedMarker + Convert.ToBase64String(encryptedTokenBytes);

                var configCopy = new Config()
                {
                    Token = encryptedToken,
                    Player = config.Player,
                    Studio = config.Studio,
                    Website = config.Website,
                };
                // serialize that into the file
                fs.SetLength(0);
                await JsonSerializer.SerializeAsync(fs, configCopy, ConfigSerializerContext.Default.Options);
            }
        }

        return config;
    }
}
