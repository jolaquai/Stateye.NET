using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

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

internal static partial class ConfigLog
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Debug, Message = "Config file not found")]
    public static partial void ConfigFileNotFound(ILogger logger);

    [LoggerMessage(EventId = 2, Level = LogLevel.Debug, Message = "Loaded config from {ConfigPath}")]
    public static partial void LoadedConfig(ILogger logger, string configPath);

    [LoggerMessage(EventId = 3, Level = LogLevel.Debug, Message = "Decrypting token from config file")]
    public static partial void DecryptingToken(ILogger logger);

    [LoggerMessage(EventId = 4, Level = LogLevel.Debug, Message = "Token in config file is not encrypted, so we're doing that")]
    public static partial void EncryptingPlaintextToken(ILogger logger);
}

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
    /// Loads configuration from the specified config file.
    /// Falls back to defaults if the file is missing or malformed.
    /// </summary>
    public static Task<Config> LoadAsync(ILogger logger = null, CancellationToken cancellationToken = default)
        => LoadAsync(Path.Combine(Environment.CurrentDirectory, AppConstants.ConfigFileName), logger, cancellationToken);

    /// <summary>
    /// Loads configuration from the specified config file.
    /// Falls back to defaults if the file is missing or malformed.
    /// </summary>
    public static async Task<Config> LoadAsync(string configPath, ILogger logger = null, CancellationToken cancellationToken = default)
    {
        logger ??= NullLogger.Instance;
        ArgumentException.ThrowIfNullOrWhiteSpace(configPath);

        if (!File.Exists(configPath))
        {
            ConfigLog.ConfigFileNotFound(logger);
            return null;
        }

        ConfigLog.LoadedConfig(logger, configPath);

        await using var fs = new FileStream(configPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None, 4096, FileOptions.Asynchronous);
        var config = await JsonSerializer.DeserializeAsync(fs, ConfigSerializerContext.Default.Config, cancellationToken);

        if (config.Token is string token && token.Length > TokenIsEncryptedMarker.Length)
        {
            if (config.Token.StartsWith(TokenIsEncryptedMarker))
            {
                ConfigLog.DecryptingToken(logger);

                // span out the encrypted token and decrypt it
                var encryptedTokenSpan = token[TokenIsEncryptedMarker.Length..];
                var encryptedTokenBytes = Convert.FromBase64String(encryptedTokenSpan);
                var decryptedTokenBytes = ProtectedData.Unprotect(encryptedTokenBytes, null, DataProtectionScope.CurrentUser);
                config.Token = Encoding.ASCII.GetString(decryptedTokenBytes);
            }
            else
            {
                ConfigLog.EncryptingPlaintextToken(logger);

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
                fs.Position = 0;
                await JsonSerializer.SerializeAsync(fs, configCopy, ConfigSerializerContext.Default.Config, cancellationToken);
            }
        }

        return config;
    }

    public static async Task WriteAsync(string configPath, Config config, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configPath);
        ArgumentNullException.ThrowIfNull(config);

        var directoryPath = Path.GetDirectoryName(configPath);
        if (!string.IsNullOrWhiteSpace(directoryPath))
            Directory.CreateDirectory(directoryPath);

        await using var fs = new FileStream(configPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous);
        await JsonSerializer.SerializeAsync(fs, config, ConfigSerializerContext.Default.Config, cancellationToken);
    }
}
