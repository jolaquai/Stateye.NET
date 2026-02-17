using DiscordRPC;

namespace Stateye;

/// <summary>
/// Helper for building and setting Discord Rich Presence activities.
/// </summary>
public static class DiscordActivity
{
    public static void SetActivity(
        DiscordRpcClient client,
        string details,
        string state,
        string bigIconUrl,
        string smallIconUrl,
        DiscordRPC.Button[]? buttons,
        long elapsedEpochSeconds)
    {
        var presence = new RichPresence
        {
            State = state,
            Timestamps = new Timestamps(DateTime.UtcNow - TimeSpan.FromSeconds(
                DateTimeOffset.UtcNow.ToUnixTimeSeconds() - elapsedEpochSeconds))
        };

        if (!string.IsNullOrEmpty(details))
            presence.Details = details;

        if (!string.IsNullOrEmpty(bigIconUrl) || !string.IsNullOrEmpty(smallIconUrl))
        {
            presence.Assets = new Assets();

            if (!string.IsNullOrEmpty(bigIconUrl))
                presence.Assets.LargeImageKey = bigIconUrl;
        }

        if (buttons is { Length: > 0 })
            presence.Buttons = buttons;

        client.SetPresence(presence);
    }

    public static long GetEpochSeconds()
    {
        return DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    }
}
