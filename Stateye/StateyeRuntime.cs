using System.Security.Cryptography;
using System.Text.Json;
using DiscordRPC;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Stateye;

internal static partial class StateyeRuntimeLog
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Debug, Message = "Application started")]
    public static partial void ApplicationStarted(ILogger logger);

    [LoggerMessage(EventId = 2, Level = LogLevel.Critical, Message = "Failed to load config due to a CryptographicException. This likely means that you've changed your Windows account password or you're not the person who had their token encrypted where you ran this application.")]
    public static partial void ConfigCryptoFailure(ILogger logger);

    [LoggerMessage(EventId = 3, Level = LogLevel.Critical, Message = "Please update the configured token value with your own unencrypted token from the Roblox website, then re-run the application. File-backed configs will encrypt that token on first read so that only your user account can decrypt it (until your Windows password changes).")]
    public static partial void ConfigCryptoRecovery(ILogger logger);

    [LoggerMessage(EventId = 4, Level = LogLevel.Critical, Message = "Failed to load config: [{ExceptionType}] '{ExceptionMessage}'")]
    public static partial void ConfigLoadFailure(ILogger logger, string exceptionType, string exceptionMessage);

    [LoggerMessage(EventId = 5, Level = LogLevel.Critical, Message = "{Message}")]
    public static partial void CriticalMessage(ILogger logger, string message);

    [LoggerMessage(EventId = 6, Level = LogLevel.Critical, Message = "Please ensure that your configured config source is valid and try again.")]
    public static partial void ConfigLoadRecovery(ILogger logger);

    [LoggerMessage(EventId = 7, Level = LogLevel.Critical, Message = "No valid token found. You'll need to get one from the Roblox website https://www.roblox.com/ by logging in, inspecting application storage and getting your auth token.")]
    public static partial void NoValidToken(ILogger logger);

    [LoggerMessage(EventId = 8, Level = LogLevel.Critical, Message = "Provide a token through the configured Stateye config source. If that source is file-backed, a sample config will be written when possible and the token will be encrypted on first read.")]
    public static partial void NoValidTokenRecovery(ILogger logger);

    [LoggerMessage(EventId = 16, Level = LogLevel.Critical, Message = "Wrote sample config to {ConfigPath}")]
    public static partial void SampleConfigWritten(ILogger logger, string configPath);

    [LoggerMessage(EventId = 9, Level = LogLevel.Debug, Message = "Authenticated as user {UserId}")]
    public static partial void Authenticated(ILogger logger, long userId);

    [LoggerMessage(EventId = 10, Level = LogLevel.Debug, Message = "Presence or universe changed")]
    public static partial void PresenceOrUniverseChanged(ILogger logger);

    [LoggerMessage(EventId = 11, Level = LogLevel.Debug, Message = "Activity changed (browsing website)")]
    public static partial void BrowsingWebsite(ILogger logger);

    [LoggerMessage(EventId = 12, Level = LogLevel.Debug, Message = "Place Info: {PlaceInfo}")]
    public static partial void PlaceInfo(ILogger logger, PlaceInfo placeInfo);

    [LoggerMessage(EventId = 13, Level = LogLevel.Debug, Message = "Place Icon URL: {PlaceIconUrl}")]
    public static partial void PlaceIconUrl(ILogger logger, string placeIconUrl);

    [LoggerMessage(EventId = 14, Level = LogLevel.Debug, Message = "Activity changed (in game '{PlaceName}' since '{StartTimestamp}')")]
    public static partial void InGameActivityChanged(ILogger logger, string placeName, DateTimeOffset startTimestamp);

    [LoggerMessage(EventId = 15, Level = LogLevel.Debug, Message = "Activity changed (in studio '{PlaceInfo}' since '{StartTimestamp}')")]
    public static partial void InStudioActivityChanged(ILogger logger, PlaceInfo placeInfo, DateTimeOffset startTimestamp);
}

public sealed class StateyeRuntime(StateyeRuntimeOptions options, ILogger<StateyeRuntime> logger)
{
    private readonly StateyeRuntimeOptions _options = options ?? StateyeRuntimeOptions.Default;
    private readonly ILogger<StateyeRuntime> _logger = logger ?? NullLogger<StateyeRuntime>.Instance;

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        StateyeRuntimeLog.ApplicationStarted(_logger);

        Config config;
        try
        {
            config = await _options.LoadConfigAsync(_logger, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            var baseEx = ex.GetBaseException();
            if (baseEx is CryptographicException)
            {
                StateyeRuntimeLog.ConfigCryptoFailure(_logger);
                StateyeRuntimeLog.ConfigCryptoRecovery(_logger);
                return;
            }

            StateyeRuntimeLog.ConfigLoadFailure(_logger, baseEx.GetType().FullName, baseEx.Message);
            if (!string.IsNullOrWhiteSpace(baseEx.StackTrace))
                StateyeRuntimeLog.CriticalMessage(_logger, baseEx.StackTrace);
            StateyeRuntimeLog.ConfigLoadRecovery(_logger);
            return;
        }

        var token = RbxCookie.GetValue() ?? config?.Token;

        if (string.IsNullOrWhiteSpace(token))
        {
            StateyeRuntimeLog.NoValidToken(_logger);
            StateyeRuntimeLog.NoValidTokenRecovery(_logger);
            var sampleConfigObject = new Config() { Token = "[YOUR TOKEN HERE]" };
            var sampleConfig = JsonSerializer.Serialize(sampleConfigObject, ConfigSerializerContext.Default.Config);
            StateyeRuntimeLog.CriticalMessage(_logger, $"""
                It needs to look something like this:
                {sampleConfig}
                """);
            var sampleConfigPath = await _options.WriteSampleConfigAsync(sampleConfigObject, cancellationToken);
            if (!string.IsNullOrWhiteSpace(sampleConfigPath))
                StateyeRuntimeLog.SampleConfigWritten(_logger, sampleConfigPath);
            return;
        }

        using var robloxClient = new RobloxApi(token);

        using var robloxPlayer = new DiscordRpcClient(AppConstants.PlayerDiscordAppId);
        using var robloxStudio = new DiscordRpcClient(AppConstants.StudioDiscordAppId);

        robloxPlayer.Initialize();
        robloxStudio.Initialize();

        var universeChanged = false;
        long lastRobloxUniverseId = 0;
        var lastRobloxPresenceType = PresenceType.Offline;
        var startTimestamp = DiscordActivity.GetEpochSeconds();

        AuthInfo authInfo = null;
        do
        {
            try
            {
                authInfo = await robloxClient.GetUserAuthInfoAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                await Task.Delay(2_000, cancellationToken);
            }
        } while (authInfo is null);

        StateyeRuntimeLog.Authenticated(_logger, authInfo.Id);

        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(AppConstants.FrequencyOfStatusUpdates));

        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                UserPresence userPresence;
                try
                {
                    userPresence = await robloxClient.GetUserPresenceAsync(authInfo.Id, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch
                {
                    await Task.Delay(2_000, cancellationToken);
                    continue;
                }

                if (lastRobloxPresenceType != userPresence.PresenceType || universeChanged)
                {
                    StateyeRuntimeLog.PresenceOrUniverseChanged(_logger);

                    lastRobloxPresenceType = userPresence.PresenceType;
                    universeChanged = false;
                    startTimestamp = DiscordActivity.GetEpochSeconds();
                }
                else
                    continue;

                if (userPresence.PresenceType == PresenceType.Online && config.Website)
                {
                    DiscordActivity.SetActivity(
                        robloxPlayer,
                        "Browsing", "Website",
                        Resources.RobloxIconUrl, "",
                        null,
                        startTimestamp);

                    StateyeRuntimeLog.BrowsingWebsite(_logger);
                }
                else if (userPresence.PresenceType == PresenceType.InGame && config.Player)
                {
                    var universeId = userPresence.UniverseId.Value;

                    try
                    {
                        var placeInfo = await robloxClient.GetPlaceInfoAsync(userPresence.PlaceId.Value, cancellationToken);
                        var placeIconUrl = await robloxClient.GetPlaceIconUrlAsync(universeId, cancellationToken);

                        if (universeId != lastRobloxUniverseId)
                        {
                            universeChanged = true;
                            lastRobloxUniverseId = universeId;
                        }

                        StateyeRuntimeLog.PlaceInfo(_logger, placeInfo);
                        StateyeRuntimeLog.PlaceIconUrl(_logger, placeIconUrl);

                        DiscordActivity.SetActivity(
                            robloxPlayer,
                            "", placeInfo.Name,
                            placeIconUrl, "",
                            [new Button { Label = "Game Page", Url = placeInfo.Url }],
                            startTimestamp);

                        StateyeRuntimeLog.InGameActivityChanged(_logger, placeInfo.Name, DateTimeOffset.FromUnixTimeSeconds(startTimestamp));
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch
                    {
                    }
                }
                else if (userPresence.PresenceType == PresenceType.InStudio && config.Studio)
                {
                    try
                    {
                        var placeInfo = await robloxClient.GetPlaceInfoAsync(userPresence.PlaceId.Value, cancellationToken);

                        DiscordActivity.SetActivity(
                            robloxStudio,
                            "Developing", placeInfo.Name,
                            Resources.RobloxStudioIconUrl, "",
                            [new Button { Label = "Game Page", Url = placeInfo.Url }],
                            startTimestamp);

                        StateyeRuntimeLog.InStudioActivityChanged(_logger, placeInfo, DateTimeOffset.FromUnixTimeSeconds(startTimestamp));
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch
                    {
                    }
                }
                else
                {
                    robloxPlayer.ClearPresence();
                    robloxStudio.ClearPresence();
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }
}
