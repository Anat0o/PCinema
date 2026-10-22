using System.Collections.ObjectModel;
namespace PCinema;

public sealed class LibraryViewModel(LibraryStore store) : Observable
{
    public ObservableCollection<Series> All { get; } = []; public ObservableCollection<Series> Visible { get; } = []; public ObservableCollection<Series> Continuing { get; } = []; public ObservableCollection<Series> Recent { get; } = [];
    string search = "", filter = "Все", status = "Ваша коллекция. Ваш ритм.";
    public string Search { get => search; set { search = value; Changed(); ApplyFilter(); } }
    public string Filter { get => filter; set { filter = value; Changed(); ApplyFilter(); } }
    public string Status { get => status; set { status = value; Changed(); } }
    public Series? Hero => Continuing.FirstOrDefault() ?? Recent.FirstOrDefault();
    public bool Empty => All.Count == 0; public string HeroTitle => Hero?.Title ?? "Ваш личный кинотеатр";
    public string HeroOverview => Hero?.Overview ?? "Добавьте папку с сериалами. PCinema соберёт вашу коллекцию и запомнит, где вы остановились.";
    public string HeroAction => Hero == null ? "Добавить сериалы" : Hero.State == "В процессе" ? "Продолжить" : "Смотреть";
    public void Reload(List<Series>? data = null) { All.Clear(); foreach (var s in data ?? store.LoadCatalog()) All.Add(s); Continuing.Clear(); foreach (var s in All.Where(x => x.State == "В процессе").OrderByDescending(x => x.Episodes.Max(e => e.LastPlayed)).Take(24)) Continuing.Add(s); Recent.Clear(); foreach (var s in All.OrderByDescending(x => x.Episodes.Max(e => e.Id)).Take(12)) Recent.Add(s); ApplyFilter(); Changed(nameof(Hero)); Changed(nameof(HeroTitle)); Changed(nameof(HeroOverview)); Changed(nameof(Empty)); Changed(nameof(HeroAction)); }
    void ApplyFilter() { Visible.Clear(); foreach (var s in All.Where(x => (Filter == "Все" || x.State == Filter) && (x.Title.Contains(Search, StringComparison.OrdinalIgnoreCase) || x.SearchTitle.Contains(Search, StringComparison.OrdinalIgnoreCase))).OrderBy(x => x.Title)) Visible.Add(s); }
}

