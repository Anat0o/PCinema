namespace PCinema;

/// <summary>Global visibility preference; track IDs belong to individual media files.</summary>
public sealed class SubtitlePreferences(LibraryStore store)
{
    public bool Disabled { get; private set; } = store.Setting("subtitles_disabled") == "true";

    public void SelectTrack(int id)
    {
        Disabled = id < 0;
        store.Setting("subtitles_disabled", Disabled ? "true" : "false");
    }
}
