using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
namespace PCinema;
public static class Ui
{
    public static readonly Brush Muted = new SolidColorBrush(Color.FromRgb(166,169,175));
    public static TextBlock Text(string value, double size = 14, bool bold = false) => new() { Text=value, FontSize=size, FontWeight=bold ? FontWeights.SemiBold : FontWeights.Normal, TextWrapping=TextWrapping.Wrap, Margin=new(0,0,0,12) };
    public static Button Button(string text, Action action, bool primary = false) { var b = new Button { Content=text }; if(primary) b.Style=(Style)Application.Current.FindResource("PrimaryButton"); b.Click+=(_,_)=>action(); return b; }
    public static void Enter(UIElement view) { if(SystemParameters.ClientAreaAnimation) view.BeginAnimation(UIElement.OpacityProperty,new DoubleAnimation(0,1,TimeSpan.FromMilliseconds(160)){FillBehavior=FillBehavior.Stop}); }
    public static Border Panel(UIElement content) => new() { Background=new SolidColorBrush(Color.FromArgb(160,35,37,41)), BorderBrush=new SolidColorBrush(Color.FromArgb(24,255,255,255)), BorderThickness=new(1), CornerRadius=new(18), Padding=new(24), Child=content, Margin=new(0,0,0,20) };
}
public class Screen : UserControl
{
    public MainWindow Host { get; }
    public Screen(MainWindow host) { Host=host; Foreground=Brushes.White; }
    public virtual void RefreshData() { }
    public virtual void Leave() { }
}

