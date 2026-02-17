using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace Stateye;

public enum PresenceType
{
    Offline = 0,
    Online = 1,
    InGame = 2,
    InStudio = 3
}

public sealed record AuthInfo(long Id);

public sealed record PlaceInfo(string Name, string Url);

public sealed record UserPresence(PresenceType PresenceType, long? PlaceId, long? UniverseId);

/// <summary>
/// HTTP client wrapper for the Roblox web APIs.
/// </summary>
public sealed class RobloxApi
{
    private readonly HttpClient _client;
    private readonly string _token;

    public RobloxApi(string token)
    {
        _token = token;
        _client = new HttpClient();
    }

    private string TokenCookie => $".ROBLOSECURITY={_token}";

    public async Task<AuthInfo> GetUserAuthInfoAsync()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://users.roblox.com/v1/users/authenticated");
        request.Headers.Add("Cookie", TokenCookie);
        request.Headers.Add("Accept", "application/json");

        var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var id = doc.RootElement.GetProperty("id").GetInt64();

        return new AuthInfo(id);
    }

    public async Task<string> GetPlaceIconUrlAsync(long universeId)
    {
        var url = $"https://thumbnails.roblox.com/v1/games/icons?universeIds={universeId}&size=512x512&format=Png&isCircular=false";

        var response = await _client.GetAsync(url);
        response.EnsureSuccessStatusCode();

        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var imageUrl = doc.RootElement
            .GetProperty("data")[0]
            .GetProperty("imageUrl")
            .GetString()!;

        return imageUrl;
    }

    public async Task<PlaceInfo> GetPlaceInfoAsync(long placeId)
    {
        var url = $"https://games.roblox.com/v1/games/multiget-place-details?placeIds={placeId}";

        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("Cookie", TokenCookie);

        var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var first = doc.RootElement[0];

        return new PlaceInfo(
            Name: first.GetProperty("name").GetString()!,
            Url: first.GetProperty("url").GetString()!
        );
    }

    public async Task<UserPresence> GetUserPresenceAsync(long userId)
    {
        var url = "https://presence.roblox.com/v1/presence/users";

        var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Add("Cookie", TokenCookie);
        request.Content = new StringContent(
            $"{{\"userIds\":[{userId}]}}",
            Encoding.UTF8,
            "application/json"
        );

        var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var presence = doc.RootElement
            .GetProperty("userPresences")[0];

        var presenceTypeId = presence.GetProperty("userPresenceType").GetInt32();
        var presenceType = presenceTypeId switch
        {
            1 => PresenceType.Online,
            2 => PresenceType.InGame,
            3 => PresenceType.InStudio,
            _ => PresenceType.Offline
        };

        long? placeId = presence.TryGetProperty("placeId", out var pid) && pid.ValueKind == JsonValueKind.Number
            ? pid.GetInt64()
            : null;

        long? universeId = presence.TryGetProperty("universeId", out var uid) && uid.ValueKind == JsonValueKind.Number
            ? uid.GetInt64()
            : null;

        return new UserPresence(presenceType, placeId, universeId);
    }
}
