using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PCinema;
using PCinema.Modules;
using PCinema.ModuleApi;

internal static class Program
{
    static IEnumerable<Screen> Screens(DependencyObject element){if(element is Screen screen)yield return screen;for(int i=0;i<VisualTreeHelper.GetChildrenCount(element);i++)foreach(var child in Screens(VisualTreeHelper.GetChild(element,i)))yield return child;}
    [STAThread] static int Main(string[] args)
    {
        var output=Path.GetFullPath(args.FirstOrDefault()??"artifacts/design-check");Directory.CreateDirectory(output);
        Environment.SetEnvironmentVariable("PCINEMA_DATA_DIR",Path.Combine(output,"profile"));
        var store=new LibraryStore();store.Setting("tmdb_mode","off");
        for(int i=1;i<=8;i++)
        {
            var path=Path.Combine(output,$"fixture-{i}.mkv");File.WriteAllBytes(path,[0]);store.Import(path,new(i==1?"Очень длинное название сериала для проверки переноса и обрезки в интерфейсе":"Тестовый сериал "+i,2020+i,1,i));
        }
        var all=store.Load();new ProgressService(store).Save(all[0].Episodes[0],42000,120000);
        store.Execute("UPDATE episodes SET available=0 WHERE series_id=$i",("$i",all.Last().Id));
        var app=new App();app.InitializeComponent();ThemeValues.Reset();SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var frame=new DispatcherFrame();Exception? failure=null;
        Dispatcher.CurrentDispatcher.BeginInvoke(async()=>
        {
            try
            {
                var host=new MainWindow();var tmdb=new TmdbService(store);((LibraryViewModel)host.DataContext).Reload(store.Load());
                var cases=new (string Name,Action Open)[]{("home",()=>host.ModuleNavigateAsync(Route.Home,null).GetAwaiter().GetResult()),("library",()=>host.Navigate(new LibraryScreen(host,store))),("history",()=>host.Navigate(new HistoryScreen(host,store))),("series",()=>host.Navigate(new SeriesWindow(host,store,tmdb,store.Load().First()))),("archive",()=>host.Navigate(new SeriesWindow(host,store,tmdb,store.Load().Last()))),("general",()=>host.OpenSettings()),("playback",()=>host.OpenSettings("Воспроизведение")),("sources",()=>host.OpenSettings("Библиотека")),("modules",()=>host.OpenSettings("Модули")),("account",()=>host.OpenSettings("Аккаунт и синхронизация")),("extensions",()=>host.Navigate(new ExtensionsScreen(host)))};
                var root=(FrameworkElement)host.Content;((Panel)root).Background=(Brush)app.FindResource("App.Background");
                foreach(var test in cases)
                {
                    test.Open();root.Width=1280;root.Height=860;root.Measure(new Size(1280,860));root.Arrange(new Rect(0,0,1280,860));root.UpdateLayout();foreach(var screen in Screens(root).ToArray()){screen.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));screen.RefreshData();}await Task.Delay(350);
                    foreach(var size in new[]{new Size(850,600),new Size(1280,860),new Size(2560,1600)})
                    {
                        root.Width=size.Width;root.Height=size.Height;root.Measure(size);root.Arrange(new Rect(size));root.UpdateLayout();await Task.Delay(220);root.UpdateLayout();
                        if(test.Name=="home")
                        {
                            // Inject decoded art into the real layout: unloaded headless controls do not run AsyncImage.
                            foreach(var dimensions in new[]{(1600,900),(900,1600)})
                            {
                                var pixels=new byte[dimensions.Item1*dimensions.Item2*4];
                                for(int i=0;i<pixels.Length;i+=4){pixels[i]=65;pixels[i+1]=45;pixels[i+2]=30;pixels[i+3]=255;}
                                var art=BitmapSource.Create(dimensions.Item1,dimensions.Item2,96,96,PixelFormats.Bgra32,null,pixels,dimensions.Item1*4);art.Freeze();
                                var surface=host.FindName("HomeArtwork");
                                if(surface is Image image)image.Source=art;
                                else if(surface is Border border)border.Background=new ImageBrush(art){Stretch=Stretch.UniformToFill};
                                root.Measure(size);root.Arrange(new Rect(size));root.UpdateLayout();
                                var backdrop=(Image)host.FindName("HomeArtwork");
                                var expected=backdrop.ActualWidth*dimensions.Item2/dimensions.Item1;
                                if(Math.Abs(backdrop.ActualHeight-expected)>1)throw new InvalidOperationException("Background aspect ratio changed.");
                                var banner=(FrameworkElement)host.FindName("HomeBanner");
                                if(banner.ActualHeight>550)throw new InvalidOperationException($"Backdrop inflated home banner to {banner.ActualHeight} at {size.Width}, image {dimensions}.");
                            }
                        }
                        foreach(var scale in new[]{1d,1.5d,2d})
                        {
                            var bitmap=new RenderTargetBitmap((int)(size.Width*scale),(int)(size.Height*scale),96*scale,96*scale,PixelFormats.Pbgra32);bitmap.Render(root);
                            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(Path.Combine(output,$"{test.Name}-{size.Width}-{scale*100}.png"));encoder.Save(file);
                        }
                    }
                }
                await host.Modules.StopAsync();await host.Progress.FlushAsync();await host.Playback.DisposeAsync();tmdb.Dispose();Console.WriteLine("Rendered 11 screens at 850x600 and 1280x860 and 2560x1600, 100/150/200% raster scale. No Google login or network tests.");
            }
            catch(Exception ex){failure=ex;Console.Error.WriteLine(ex);}
            finally{frame.Continue=false;}
        });
        Dispatcher.PushFrame(frame);return failure==null?0:1;
    }
}
