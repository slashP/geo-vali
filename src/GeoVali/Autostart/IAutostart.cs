namespace GeoVali.Autostart;

public interface IAutostart
{
    bool IsEnabled();
    void Enable();
    void Disable();

    /// <summary>One sentence for the settings screen saying what enabling this actually does.</summary>
    string Describe();
}
