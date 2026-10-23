namespace PCinema;

/// <summary>Global visibility preference; track IDs belong to individual media files.</summary>
public sealed class SubtitlePreferences(LibraryStore store,bool? initialDisabled=null)
{
    public bool Disabled { get; private set; } = initialDisabled ?? store.Setting("subtitles_disabled") == "true";
    public void LoadDisabled(bool value)=>Disabled=value;

    readonly SerialWorkQueue writes=new();
    public Task SelectTrackAsync(int id)
    {
        Disabled=id<0;var value=Disabled?"true":"false";
        return writes.Run(()=>{try{store.Setting("subtitles_disabled",value);}catch(Exception ex){PerformanceLog.Write("subtitles.save.failed",error:ex);}});
    }
    public void SelectTrack(int id)
    {
        Disabled = id < 0;
        store.Setting("subtitles_disabled", Disabled ? "true" : "false");
    }
}
