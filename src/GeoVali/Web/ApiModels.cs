namespace GeoVali.Web;

public sealed record StatusResponse(
    bool setupComplete,
    string? mapsRoot,
    bool valiInstalled,
    string valiInstallCommand,
    bool authValid,
    string? userNick,
    DateTime? nextRunUtc,
    bool running,
    string? currentMap,
    string version);

public sealed record MapRow(
    string directory,
    string folderName,
    bool configured,
    string name,
    bool published,
    int cadenceDays,
    DateTime? lastPublishedTimeUtc,
    int updateCount,
    string? lastError,
    bool due);

public sealed record RunRequestBody(string? scope, string? directory);

public sealed record FolderBody(string? path);

public sealed record CookieBody(string? cookie);

public sealed record CreateMapBody(string? directory, string? name, string? description);

public sealed record LinkMapBody(string? directory, string? url);

public sealed record SettingsBody(
    string? mapsRoot,
    int? defaultCadenceDays,
    int? checkIntervalMinutes,
    int? dashboardPort,
    bool? startAtLogin);
