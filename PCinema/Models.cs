using System.ComponentModel;
using System.Runtime.CompilerServices;
namespace PCinema;

public class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}
public record ParsedName(string Title, int? Year, int? Season, int? Number);
public record LibraryFolder(long Id, string Path);
public record Season(int Number, IReadOnlyList<Episode> Episodes);
public sealed class Series
{
    public long Id { get; set; }
    public string Title { get; set; } = ""; public string SearchTitle { get; set; } = ""; public int? Year { get; set; }
    public int? TmdbId { get; set; }
    public string Overview { get; set; } = "Описание появится после подключения TMDB."; public string? Poster { get; set; }
    public string? Backdrop { get; set; } public string? Logo { get; set; } public string Genres { get; set; } = ""; public double Rating { get; set; }
    public string Details => string.Join("  ·  ", new[] { Year?.ToString(), Genres, Rating > 0 ? $"TMDB {Rating:0.0}" : null }.Where(x => !string.IsNullOrWhiteSpace(x)));
    public List<Episode> Episodes { get; set; } = [];
    public string Subtitle => $"{(Year.HasValue ? Year + "  ·  " : "")}Серий: {Episodes.Count}";
    public string State => Episodes.Count > 0 && Episodes.All(x => x.Watched) ? "Просмотрено" : Episodes.Any(x => x.Position > 0 || x.Watched) ? "В процессе" : "Не начато";
    public double Progress => Episodes.Count == 0 ? 0 : Episodes.Average(x => x.Watched ? 100 : x.Duration > 0 ? 100.0 * x.Position / x.Duration : 0);
    public string ProgressLabel => $"{Episodes.Count(x => x.Watched)} / {Episodes.Count} просмотрено";
}
public sealed class Episode
{
    public long Id { get; set; }
    public long SeriesId { get; set; }
    public int? Season { get; set; }
    public int? Number { get; set; }
    public string Path { get; set; } = ""; public bool Available { get; set; }
    public long Position { get; set; }
    public long Duration { get; set; }
    public bool Watched { get; set; }
    public string LastPlayed { get; set; } = "";
    public string Label => Season.HasValue && Number.HasValue ? $"S{Season:00} · E{Number:00}" : "Номер не определён";
    public string? Title { get; set; } public string? Still { get; set; }
    public string DisplayTitle => string.IsNullOrWhiteSpace(Title) ? FileName : Title;
    public string FileName => System.IO.Path.GetFileName(Path);
    public string Status => !Available ? "Файл недоступен" : Watched ? "✓ Просмотрено" : Position > 0 ? $"Продолжить с {TimeSpan.FromMilliseconds(Position):hh\\:mm\\:ss}" : "Не начато";
}
public record Metadata(int Id, string Name, string OriginalName, int? Year, string Overview, string? Poster, string? Backdrop)
{ public override string ToString() => $"{Name} ({Year?.ToString() ?? "—"}) · {OriginalName}"; }

