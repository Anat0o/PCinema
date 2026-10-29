using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
namespace PCinema;
public static class Ui
{
    public static Brush Muted => (Brush)Application.Current.FindResource("App.Muted");
    public static TextBlock Text(string value, double size = 14, bool bold = false) { var text=new TextBlock { Text=value, FontWeight=bold ? FontWeights.SemiBold : FontWeights.Normal, TextWrapping=TextWrapping.Wrap, Margin=new(0,0,0,12) }; if(size==14)text.SetResourceReference(TextBlock.FontSizeProperty,"App.FontSize");else text.FontSize=size; return text; }
    public static Button Button(string text, Action action, bool primary = false) { var b = new Button { Content=text }; if(primary) { b.Style=(Style)Application.Current.FindResource("PrimaryButton"); b.Resources["App.ButtonRadius"]=new CornerRadius(24); } b.Click+=(_,_)=>action(); return b; }
    public static FrameworkElement SettingRow(string title,string description,FrameworkElement control)
    {
        var grid=new Grid{Margin=new(0,8,0,12)};grid.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});grid.ColumnDefinitions.Add(new(){Width=new(170)});
        var copy=new StackPanel{Margin=new(0,0,20,0)};copy.Children.Add(Text(title,16,true));var note=Text(description,13);note.SetResourceReference(TextBlock.ForegroundProperty,"App.Muted");copy.Children.Add(note);grid.Children.Add(copy);
        control.VerticalAlignment=VerticalAlignment.Center;control.HorizontalAlignment=HorizontalAlignment.Right;System.Windows.Automation.AutomationProperties.SetName(control,title);Grid.SetColumn(control,1);grid.Children.Add(control);return grid;
    }
    public static void Enter(UIElement view) { if(SystemParameters.ClientAreaAnimation) view.BeginAnimation(UIElement.OpacityProperty,new DoubleAnimation(0,1,TimeSpan.FromMilliseconds(Application.Current.TryFindResource("App.Animation") is double ms?ms:160)){FillBehavior=FillBehavior.Stop}); }
    public static Border Panel(UIElement content) {var b=new Border{BorderThickness=new(1),Padding=new(24),Child=content,Margin=new(0,0,0,20)};b.SetResourceReference(Border.BackgroundProperty,"App.Panel");b.SetResourceReference(Border.BorderBrushProperty,"App.Border");b.SetResourceReference(Border.CornerRadiusProperty,"App.Radius");return b;}
}
public class Screen : UserControl
{
    public MainWindow Host { get; }
    public Screen(MainWindow host) { Host=host; SetResourceReference(ForegroundProperty,"App.Text"); SetResourceReference(FontFamilyProperty,"App.Font"); }
    public virtual void RefreshData() { }
    public virtual void Leave() { }
}

