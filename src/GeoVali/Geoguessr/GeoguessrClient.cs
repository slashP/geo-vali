using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using GeoVali.Maps;

namespace GeoVali.Geoguessr;

public sealed class GeoguessrClient(HttpClient http) : IGeoguessrClient
{
    public const string BaseAddress = "https://www.geoguessr.com/";

    private static readonly JsonSerializerOptions Body = new() { WriteIndented = false };

    public async Task<string?> GetSignedInUserNickAsync(CancellationToken ct)
    {
        using var response = await http.GetAsync("api/v3/profiles", ct);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            return null;
        }

        await EnsureSuccess(response, "reading your GeoGuessr profile", ct);
        var node = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct));
        return node?["user"]?["nick"]?.GetValue<string>() ?? node?["nick"]?.GetValue<string>();
    }

    public async Task<DraftInfo?> GetDraftAsync(string mapId, CancellationToken ct)
    {
        using var response = await http.GetAsync($"api/v4/user-maps/drafts/{mapId}", ct);
        if (response.StatusCode is HttpStatusCode.Unauthorized)
        {
            throw new GeoguessrAuthException();
        }

        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var node = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct))
                   ?? throw new GeoguessrException($"GeoGuessr returned an empty response for map {mapId}.");

        return new DraftInfo(
            node["id"]?.GetValue<string>() ?? mapId,
            node["name"]?.GetValue<string>() ?? "",
            node["version"]?.GetValue<int>() ?? 0);
    }

    public async Task<string> PublishAsync(PublishRequest request, CancellationToken ct)
    {
        var mapId = request.MapId;

        if (string.IsNullOrEmpty(mapId))
        {
            using var created = await http.PostAsJsonAsync(
                "api/v4/user-maps/drafts",
                new { name = request.Name, mode = "coordinates" },
                Body, ct);
            await EnsureSuccess(created, $"creating the map \"{request.Name}\"", ct);

            mapId = JsonNode.Parse(await created.Content.ReadAsStringAsync(ct))?["id"]?.GetValue<string>()
                    ?? throw new GeoguessrException(
                        $"GeoGuessr created a draft for \"{request.Name}\" but did not return its id.");
        }

        // Mandatory: the API rejects a stale version, so read the current one and write back +1.
        var draft = await GetDraftAsync(mapId, ct)
                    ?? throw new GeoguessrException(
                        $"GeoGuessr has no map {mapId} on your account. " +
                        "Check the id in geoguessr.json, or clear it to create a new map.");

        var body = new DraftBody
        {
            id = mapId,
            name = request.Name,
            description = request.Description,
            published = request.Publish,
            avatar = new AvatarBody
            {
                background = request.Avatar.background,
                landscape = request.Avatar.landscape,
                ground = request.Avatar.ground,
                decoration = request.Avatar.decoration
            },
            customCoordinates = request.Locations.Select(location => new CoordinateBody
            {
                lat = location.lat,
                lng = location.lng,
                heading = location.heading,
                pitch = location.pitch,
                panoId = string.IsNullOrEmpty(location.panoId) ? null : location.panoId
            }).ToList(),
            version = draft.version + 1
        };

        using (var put = await http.PutAsJsonAsync($"api/v4/user-maps/drafts/{mapId}", body, Body, ct))
        {
            await EnsureSuccess(put, $"updating the map \"{request.Name}\"", ct);
        }

        if (request.Publish)
        {
            using var publish = await http.PutAsJsonAsync(
                $"api/v4/user-maps/drafts/{mapId}/publish", new { }, Body, ct);
            await EnsureSuccess(publish, $"publishing the map \"{request.Name}\"", ct);
        }

        return mapId;
    }

    private static async Task EnsureSuccess(HttpResponseMessage response, string what, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        if (response.StatusCode is HttpStatusCode.Unauthorized)
        {
            throw new GeoguessrAuthException();
        }

        var text = await response.Content.ReadAsStringAsync(ct);
        var trimmed = text.Length > 500 ? text[..500] + "..." : text;
        throw new GeoguessrException(
            $"GeoGuessr rejected {what}: {(int)response.StatusCode} {response.ReasonPhrase}. {trimmed}".Trim());
    }

    // The request shape GeoGuessr expects. "highlighted" and the custom-error-distance pair are
    // slashP-specific in the source pipeline; GeoVali always sends their neutral values, but the
    // fields have to be present.
    private sealed record DraftBody
    {
        public string? id { get; init; }
        public bool highlighted { get; init; }
        public string name { get; init; } = "";
        public string description { get; init; } = "";
        public AvatarBody avatar { get; init; } = new();
        public bool published { get; init; }
        public List<CoordinateBody> customCoordinates { get; init; } = [];
        public string[] tags { get; init; } = [];
        public bool hasCustomErrorDistance { get; init; }
        public int maxErrorDistance { get; init; }
        public int version { get; init; }
    }

    private sealed record AvatarBody
    {
        public string background { get; init; } = "";
        public string landscape { get; init; } = "";
        public string ground { get; init; } = "";
        public string decoration { get; init; } = "";
    }

    private sealed record CoordinateBody
    {
        public double lat { get; init; }
        public double lng { get; init; }
        public double heading { get; init; }
        public double pitch { get; init; }
        public string? panoId { get; init; }
    }
}
