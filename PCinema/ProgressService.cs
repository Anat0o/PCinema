namespace PCinema;

public sealed class ProgressService(LibraryStore store)
{
    public void Save(Episode episode, long position, long duration, bool ended = false)
    {
        episode.Position = Math.Max(0, position); episode.Duration = Math.Max(0, duration);
        if (ended) { episode.Watched = true; if (duration > 0) episode.Position = duration; }
        else if (duration > 0 && position >= duration * .95) episode.Watched = true;
        episode.LastPlayed = DateTime.UtcNow.ToString("O"); store.SaveProgress(episode);
    }
}
