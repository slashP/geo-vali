using GeoVali.Maps;

namespace GeoVali.Geoguessr;

/// <summary>
/// Picks a random map thumbnail. Called once when a map is set up; the result is persisted in
/// geoguessr.json so the thumbnail is stable across runs.
/// </summary>
public static class AvatarGenerator
{
    private static readonly string[] Backgrounds =
        ["sunset", "evening", "night", "sunrise", "day", "darknight", "morning"];

    private static readonly string[] Decorations =
        ["none", "tractor", "smalltrees", "oaktrees", "cactus", "palmtrees", "japanese"];

    private static readonly string[] Grounds =
        ["beige", "blue", "green", "yellow", "darkbrown", "water"];

    private static readonly string[] Landscapes =
    [
        "forest", "houses", "mountains", "snowmountains", "grassmountains", "skyline",
        "hills", "desserthills", "mountaintrees", "volcano", "fuji"
    ];

    public static MapAvatar Generate() => new()
    {
        background = Backgrounds[Random.Shared.Next(Backgrounds.Length)],
        decoration = Decorations[Random.Shared.Next(Decorations.Length)],
        ground = Grounds[Random.Shared.Next(Grounds.Length)],
        landscape = Landscapes[Random.Shared.Next(Landscapes.Length)]
    };
}
