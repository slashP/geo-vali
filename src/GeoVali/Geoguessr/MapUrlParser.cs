using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace GeoVali.Geoguessr;

/// <summary>
/// Pulls a map id out of whatever the user pasted: a play link, a map-maker link, or the bare id.
/// </summary>
public static partial class MapUrlParser
{
    [GeneratedRegex("^[0-9a-fA-F]{24}$")]
    private static partial Regex BareId();

    [GeneratedRegex(@"^/(?:maps|map-maker)/([0-9a-fA-F]{24})(?:/.*)?$")]
    private static partial Regex UrlPath();

    public static bool TryExtractMapId(string input, [NotNullWhen(true)] out string mapId)
    {
        mapId = string.Empty;
        var trimmed = (input ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return false;
        }

        if (BareId().IsMatch(trimmed))
        {
            mapId = trimmed;
            return true;
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) ||
            !uri.Host.EndsWith("geoguessr.com", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var match = UrlPath().Match(uri.AbsolutePath);
        if (!match.Success)
        {
            return false;
        }

        mapId = match.Groups[1].Value;
        return true;
    }
}
