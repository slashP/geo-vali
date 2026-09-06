using System.Text.Json;

namespace GeoVali.Configuration;

public sealed class ConfigStore(string directory)
{
    public const string FileName = "config.json";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private AppConfig? _cached;

    private string FilePath => Path.Combine(directory, FileName);

    /// <summary>The config as last read or written. Reads from disk once, then serves from memory.</summary>
    public AppConfig Current => _cached ??= Read();

    public AppConfig Read()
    {
        if (!File.Exists(FilePath))
        {
            return _cached = new AppConfig();
        }

        try
        {
            _cached = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(FilePath), Options) ?? new AppConfig();
        }
        catch (JsonException)
        {
            // A hand-edited config that no longer parses must not stop the tool from starting.
            _cached = new AppConfig();
        }

        return _cached;
    }

    public void Write(AppConfig config)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(config, Options));
        _cached = config;
    }
}
