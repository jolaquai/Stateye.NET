using System.Numerics;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.Intrinsics.X86;
using System.Security.Cryptography;

using DiscordRPC;

using Stateye;

internal class Program
{
    private static async Task Main(string[] args)
    {
        Log.Debug("Application started");

        // Get configuration
        Task<Config> configTask = Config.LoadAsync();
        await ((Task)configTask).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        if (configTask.IsFaulted)
        {
            var baseEx = configTask.Exception.GetBaseException();
            if (baseEx is CryptographicException)
            {
                Log.Fatal("Failed to load config due to a CryptographicException. This likely means that you've changed your Windows account password or you're not the person who had their token encrypted where you ran this application.");
                Log.Fatal("Please terminate this executable, replace the value of the 'Token' field in the 'stateye.config.json' file with your own (unencrypted) token from the Roblox website, then re-run the application. Your token will be encrypted so that only your user account can decrypt it (and only as long as you don't change your password).");
                return;
            }
            else
            {
                Log.Fatal($"Failed to load config: [{baseEx.GetType().FullName}] '{baseEx.Message}'");
                Log.Fatal(baseEx.StackTrace);
                Log.Fatal("Please ensure that your config file is valid and try again.");
                return;
            }
        }

        var config = configTask.Result;

        // Get token from environment (rbx_cookie equivalent), fall back to config
        var token = RbxCookie.GetValue() ?? config.Token;

        if (string.IsNullOrWhiteSpace(token))
        {
            Log.Fatal("No valid token found. You'll need to get one from the Roblox website by logging in, inspecting application storage and getting your auth token.");
            Log.Fatal("It will be encrypted upon first read so that it's not just sitting in plaintext in your config file.");
            return;
        }

        // Create Roblox API client
        var robloxClient = new RobloxApi(token);

        // Setup Discord IPC clients
        using var robloxPlayer = new DiscordRpcClient(AppConstants.PlayerDiscordAppId);
        using var robloxStudio = new DiscordRpcClient(AppConstants.StudioDiscordAppId);

        robloxPlayer.Initialize();
        robloxStudio.Initialize();

        var universeChanged = false;
        long lastRobloxUniverseId = 0;
        var lastRobloxPresenceType = PresenceType.Offline;
        var startTimestamp = DiscordActivity.GetEpochSeconds();

        // Get user info from token — retry until successful
        AuthInfo authInfo = null;
        do
        {
            Task<AuthInfo> authFetchTask = robloxClient.GetUserAuthInfoAsync();
            await ((Task)authFetchTask).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

            if (authFetchTask.IsCompletedSuccessfully)
                authInfo = authFetchTask.Result;
            else
                await Task.Delay(2_000);
        } while (authInfo is null);

        Log.Debug($"Authenticated as user {authInfo.Id}");

        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(AppConstants.FrequencyOfStatusUpdates));
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += delegate
        {
            cts.Cancel();
        };

        // Update the Discord activity presence periodically
        while (await timer.WaitForNextTickAsync(cts.Token))
        {
            Task<UserPresence> requestTask = robloxClient.GetUserPresenceAsync(authInfo.Id);
            await ((Task)requestTask).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

            if (requestTask.IsFaulted)
            {
                // Sometimes requests might fail — wait, then try again
                await Task.Delay(2000);
                continue;
            }
            var userPresence = requestTask.Result;

            // Reset timestamp whenever status or universe changes
            if (lastRobloxPresenceType != userPresence.PresenceType || universeChanged)
            {
                Log.Debug("Presence or universe changed");

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

                Log.Debug("Activity changed (browsing website)");
            }
            else if (userPresence.PresenceType == PresenceType.InGame && config.Player)
            {
                var universeId = userPresence.UniverseId.Value;

                try
                {
                    var placeInfo = await robloxClient.GetPlaceInfoAsync(userPresence.PlaceId.Value);
                    var placeIconUrl = await robloxClient.GetPlaceIconUrlAsync(universeId);

                    if (universeId != lastRobloxUniverseId)
                    {
                        universeChanged = true;
                        lastRobloxUniverseId = universeId;
                    }

                    Log.Debug($"Place Info: {placeInfo}");
                    Log.Debug($"Place Icon URL: {placeIconUrl}");

                    DiscordActivity.SetActivity(
                        robloxPlayer,
                        "", placeInfo.Name,
                        placeIconUrl, "",
                        [new Button { Label = "Game Page", Url = placeInfo.Url }],
                        startTimestamp);

                    Log.Debug($"Activity changed (in game '{placeInfo.Name}' since '{DateTimeOffset.FromUnixTimeSeconds(startTimestamp)}')");
                }
                catch
                {
                    // Place detail fetch failed — skip this cycle
                }
            }
            else if (userPresence.PresenceType == PresenceType.InStudio && config.Studio)
            {
                try
                {
                    var placeInfo = await robloxClient.GetPlaceInfoAsync(userPresence.PlaceId.Value);

                    DiscordActivity.SetActivity(
                        robloxStudio,
                        "Developing", placeInfo.Name,
                        Resources.RobloxStudioIconUrl, "",
                        [new Button { Label = "Game Page", Url = placeInfo.Url }],
                        startTimestamp);

                    Log.Debug($"Activity changed (in studio '{placeInfo}' since '{DateTimeOffset.FromUnixTimeSeconds(startTimestamp)}')");
                }
                catch
                {
                    // Place detail fetch failed — skip this cycle
                }
            }
            else
            {
                // The user is offline, clear their activity status
                robloxPlayer.ClearPresence();
                robloxStudio.ClearPresence();
            }
        }
    }
}