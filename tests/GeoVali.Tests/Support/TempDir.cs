namespace GeoVali.Tests.Support;

/// <summary>A throwaway directory tree that deletes itself at the end of a test.</summary>
public sealed class TempDir : IDisposable
{
    public string Path { get; }

    public TempDir()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "geovali-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    /// <summary>Creates a nested directory and returns its full path.</summary>
    public string Dir(params string[] segments)
    {
        var full = System.IO.Path.Combine(new[] { Path }.Concat(segments).ToArray());
        Directory.CreateDirectory(full);
        return full;
    }

    /// <summary>Writes a file (creating parent directories) and returns its full path.</summary>
    public string File(string relativePath, string contents)
    {
        var full = System.IO.Path.Combine(Path, relativePath);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        System.IO.File.WriteAllText(full, contents);
        return full;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // A test left a handle open; the OS temp cleaner will get it.
        }
    }
}
