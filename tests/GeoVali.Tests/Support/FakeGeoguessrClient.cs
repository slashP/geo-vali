using GeoVali.Geoguessr;

namespace GeoVali.Tests.Support;

/// <summary>An in-memory stand-in for the GeoGuessr API. No sockets.</summary>
public sealed class FakeGeoguessrClient : IGeoguessrClient
{
    /// <summary>Null makes the auth probe report an expired cookie.</summary>
    public string? Nick { get; set; } = "slashP";

    /// <summary>Map id to the draft the API should report.</summary>
    public Dictionary<string, DraftInfo> Drafts { get; } = new(StringComparer.Ordinal);

    /// <summary>Map name to the exception PublishAsync should throw for it.</summary>
    public Dictionary<string, Exception> FailuresByMapName { get; } = new(StringComparer.Ordinal);

    /// <summary>Id handed back when a request arrives with no MapId.</summary>
    public string NewMapId { get; set; } = "newly-created-map-id";

    public List<PublishRequest> Published { get; } = [];

    public int AuthProbeCount { get; private set; }

    public Task<string?> GetSignedInUserNickAsync(CancellationToken ct)
    {
        AuthProbeCount++;
        return Task.FromResult(Nick);
    }

    public Task<DraftInfo?> GetDraftAsync(string mapId, CancellationToken ct) =>
        Task.FromResult(Drafts.GetValueOrDefault(mapId));

    public Task<string> PublishAsync(PublishRequest request, CancellationToken ct)
    {
        if (FailuresByMapName.TryGetValue(request.Name, out var failure))
        {
            throw failure;
        }

        Published.Add(request);
        return Task.FromResult(request.MapId ?? NewMapId);
    }
}
