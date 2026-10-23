using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
namespace PCinema;

public sealed record HistoryEntry(Series Series,Episode Episode)
{
    public string Title=>Series.Title;
    public string Detail=>$"{Episode.Label}  ·  {(Episode.Watched?"Просмотрено":"В процессе")}{(Episode.Available?"":"  ·  Файл недоступен")}";
    public static List<HistoryEntry> From(IEnumerable<Series> series)=>series.SelectMany(s=>s.Episodes.Where(e=>!string.IsNullOrEmpty(e.LastPlayed)||e.Position>0||e.Watched).Select(e=>new HistoryEntry(s,e))).OrderByDescending(e=>e.Episode.LastPlayed,StringComparer.Ordinal).ToList();
}
public sealed class HistoryScreen : Screen
{
    readonly LibraryStore store;readonly ListBox list=new();readonly TextBlock status=Ui.Text("Загружаем историю…");readonly TextBox search=new(){Width=320,HorizontalAlignment=HorizontalAlignment.Left,ToolTip="Поиск в истории"};
    List<HistoryEntry> entries=[];bool refreshing;
    public HistoryScreen(MainWindow host,LibraryStore store):base(host)
    {
        this.store=store;var root=new DockPanel{Margin=new(56,110,40,20)};var heading=new StackPanel();heading.Children.Add(Ui.Text("История просмотра",32,true));heading.Children.Add(Ui.Text("Все просмотренные и начатые серии, включая удалённые из локальной библиотеки."));heading.Children.Add(Ui.Text("Поиск по названию",12));heading.Children.Add(search);heading.Children.Add(status);DockPanel.SetDock(heading,Dock.Top);root.Children.Add(heading);root.Children.Add(list);Content=root;
        var template=new DataTemplate(typeof(HistoryEntry));var button=new FrameworkElementFactory(typeof(Button));button.SetValue(Control.HorizontalContentAlignmentProperty,HorizontalAlignment.Stretch);button.SetValue(Control.PaddingProperty,new Thickness(18));button.AddHandler(Button.ClickEvent,new RoutedEventHandler((sender,e)=>{if(((FrameworkElement)sender).DataContext is HistoryEntry entry)Host.ShowSeries(entry.Series);}));
        var panel=new FrameworkElementFactory(typeof(StackPanel));var title=new FrameworkElementFactory(typeof(TextBlock));title.SetBinding(TextBlock.TextProperty,new Binding("Title"));title.SetValue(TextBlock.FontSizeProperty,20.0);panel.AppendChild(title);var detail=new FrameworkElementFactory(typeof(TextBlock));detail.SetBinding(TextBlock.TextProperty,new Binding("Detail"));detail.SetValue(TextBlock.ForegroundProperty,Ui.Muted);detail.SetValue(FrameworkElement.MarginProperty,new Thickness(0,8,0,0));panel.AppendChild(detail);button.AppendChild(panel);template.VisualTree=button;list.ItemTemplate=template;
        list.HorizontalContentAlignment=HorizontalAlignment.Stretch;VirtualizingPanel.SetIsVirtualizing(list,true);VirtualizingPanel.SetVirtualizationMode(list,VirtualizationMode.Recycling);search.TextChanged+=(_,_)=>Filter();Loaded+=(_,_)=>RefreshData();
    }
    void Filter(){var filtered=entries.Where(e=>e.Title.Contains(search.Text.Trim(),StringComparison.OrdinalIgnoreCase)).ToList();list.ItemsSource=filtered;status.Text=filtered.Count==0?"История пока пуста или ничего не найдено.":$"Серий в истории: {filtered.Count}";}
    public override async void RefreshData()
    {
        if(refreshing)return;refreshing=true;
        try{await Host.Progress.DrainAsync();entries=await Task.Run(()=>HistoryEntry.From(store.LoadCatalog(true)));Filter();}
        catch(Exception ex){status.Text="Не удалось прочитать историю. Повторите открытие вкладки.";_=Task.Run(()=>PerformanceLog.Write("history.load.failed",error:ex));}finally{refreshing=false;}
    }
}
