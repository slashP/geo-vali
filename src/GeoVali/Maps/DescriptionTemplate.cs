using System.Globalization;

namespace GeoVali.Maps;

/// <summary>
/// Expands tokens in a map description at publish time. The token, not the substituted text, is
/// what stays in <c>geoguessr.json</c>, so this is never applied before writing the file back.
/// </summary>
public static class DescriptionTemplate
{
    public const string LocationCountToken = "{{LocationCount}}";

    public static string Expand(string description, int locationCount) =>
        string.IsNullOrEmpty(description)
            ? string.Empty
            : description.Replace(LocationCountToken, locationCount.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
}
