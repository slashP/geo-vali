using GeoVali.Maps;

namespace GeoVali.Geoguessr;

/// <summary>The GeoGuessr session cookie is no longer valid. Aborts the whole run, never one map.</summary>
public sealed class GeoguessrAuthException()
    : Exception("Your GeoGuessr sign-in has expired. Open Settings and paste a fresh _ncfa cookie value.");

/// <summary>A GeoGuessr call failed, with the status and response text folded into the message.</summary>
public sealed class GeoguessrException(string message) : Exception(message);

/// <summary>What <c>GET /api/v4/user-maps/drafts/{id}</c> tells us.</summary>
public sealed record DraftInfo(string id, string name, int version);

/// <summary>Everything needed for one publish. <paramref name="MapId"/> is null for a brand-new map.</summary>
public sealed record PublishRequest(
    string? MapId,
    string Name,
    string Description,
    MapAvatar Avatar,
    bool Publish,
    IReadOnlyList<ValiLocation> Locations);

public interface IGeoguessrClient
{
    /// <summary>Auth probe. Returns the signed-in nick, or null when the cookie is rejected.</summary>
    Task<string?> GetSignedInUserNickAsync(CancellationToken ct);

    /// <summary>Returns null when the draft does not exist or the signed-in user does not own it.</summary>
    Task<DraftInfo?> GetDraftAsync(string mapId, CancellationToken ct);

    /// <summary>Runs the create/read/update/publish sequence. Returns the map id.</summary>
    Task<string> PublishAsync(PublishRequest request, CancellationToken ct);
}
