using System.Globalization;
using System.Windows;
using System.Windows.Controls;
namespace PCinema;

public sealed record HistoryEntry(Series Series,Episode Episode)
{
    public string Title=>Series.Title;
    public string DisplayTitle=>Series.Year is int year?$"{Title} ({year})":Title;
    public DateTimeOffset LastPlayed=>DateTimeOffset.TryParse(Episode.LastPlayed,CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal,out var date)?date:DateTimeOffset.MinValue;
    public string DateLabel=>LastPlayed==DateTimeOffset.MinValue?"—":LastPlayed.ToLocalTime().ToString("dd.MM.yyyy");
    public string EpisodeLabel=>Episode.Season.HasValue&&Episode.Number.HasValue?$"{Episode.Season} сезон · {Episode.Number} серия":Episode.Label;
    public string Detail=>$"{EpisodeLabel} · {(Episode.Watched?"Просмотрено":"В процессе")}{(Episode.Available?"":" · Файл недоступен")}";
    public string Summary
    {
        get
        {
            if(!Episode.Available)return "Файл недоступен · история сохранена";
            int remaining=Series.Episodes.Count(e=>e.Available&&!e.Watched&&e.Season==Episode.Season&&e.Number>Episode.Number);
            return remaining>0?$"Ещё серий локально в этом сезоне: {remaining}":Episode.Watched?"Просмотрено":"Просмотр начат";
        }
    }
    // Display the latest episode per title; every episode's position remains in the database.
    public static List<HistoryEntry> From(IEnumerable<Series> series)=>series.Select(s=>new {Series=s,Episode=s.Episodes.Where(e=>!string.IsNullOrEmpty(e.LastPlayed)||e.Position>0||e.Watched).OrderByDescending(e=>DateTimeOffset.TryParse(e.LastPlayed,CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal,out var date)?date:DateTimeOffset.MinValue).ThenByDescending(e=>e.Id).FirstOrDefault()}).Where(x=>x.Episode!=null).Select(x=>new HistoryEntry(x.Series,x.Episode!)).OrderByDescending(e=>e.LastPlayed).ThenBy(e=>e.Title).ToList();
}
public sealed partial class HistoryScreen : Screen
{
    readonly LibraryStore store;
    List<HistoryEntry> entries=[];bool refreshing;
    public HistoryScreen(MainWindow host,LibraryStore store):base(host)
    {
        this.store=store;InitializeComponent();HistoryTable.SizeChanged+=(_,_)=>{double width=Math.Max(400,HistoryTable.ActualWidth-20);double last=Math.Max(200,width*.35);HistoryTable.Columns[0].Width=new DataGridLength(112);HistoryTable.Columns[1].Width=new DataGridLength(Math.Max(120,width-112-last));HistoryTable.Columns[2].Width=new DataGridLength(last);};Loaded+=(_,_)=>RefreshData();
    }
    void ToggleSearch(object sender,RoutedEventArgs e)
    {
        bool show=Search.Visibility!=Visibility.Visible;Search.Visibility=show?Visibility.Visible:Visibility.Collapsed;
        SearchButton.ToolTip=show?"Закрыть поиск":"Поиск в истории";
        if(show){Search.Focus();Search.SelectAll();}else Search.Clear();
    }
    void SearchChanged(object sender,TextChangedEventArgs e){if(IsInitialized)Filter();}
    void OpenTitle(object sender,RoutedEventArgs e){if(((FrameworkElement)sender).DataContext is HistoryEntry entry)Host.ShowSeries(entry.Series);}
    void Filter()
    {
        var query=Search.Text.Trim();var filtered=entries.Where(e=>e.Title.Contains(query,StringComparison.OrdinalIgnoreCase)||e.Series.SearchTitle.Contains(query,StringComparison.OrdinalIgnoreCase)).ToList();
        HistoryTable.ItemsSource=filtered;Status.Text=$"Сериалов: {filtered.Count} · Последние просмотры сверху";
        Empty.Visibility=filtered.Count==0?Visibility.Visible:Visibility.Collapsed;Empty.Text=query.Length>0?"По этому названию ничего не найдено.":"Здесь появятся сериалы, которые вы начали смотреть.";
    }
    public override async void RefreshData()
    {
        if(refreshing)return;refreshing=true;
        try{await Host.Progress.DrainAsync();entries=await Task.Run(()=>HistoryEntry.From(store.LoadCatalog(true)));Filter();}
        catch(Exception ex){Status.Text="Не удалось прочитать историю. Повторите открытие вкладки.";_=Task.Run(()=>PerformanceLog.Write("history.load.failed",error:ex));}finally{refreshing=false;}
    }
}
