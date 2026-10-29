using System.Globalization;
using System.Windows;
using System.Windows.Media;
using PCinema.ModuleApi;
namespace PCinema.Modules;
public static class ThemeValues
{
    public static readonly Dictionary<string,string> Defaults=new()
    {
        [ThemeKeys.Background]="#0B0D12",[ThemeKeys.Panel]="#171B24",[ThemeKeys.Text]="#F4F6FA",[ThemeKeys.Muted]="#A8B0BF",[ThemeKeys.Accent]="#F4F4F4",
        [ThemeKeys.Glass]="#EB171B24",[ThemeKeys.Border]="#24FFFFFF",[ThemeKeys.Hover]="#16FFFFFF",
        [ThemeKeys.FontSize]="14",[ThemeKeys.Radius]="16",[ThemeKeys.Animation]="200"
    };
    public static void Validate(IReadOnlyDictionary<string,string> tokens)
    {
        foreach(var (key,value) in tokens)
        {
            if(!Defaults.ContainsKey(key))throw new System.IO.InvalidDataException("Неизвестный параметр темы: "+key);
            if(key is ThemeKeys.FontSize or ThemeKeys.Radius or ThemeKeys.Animation)
            {
                if(!double.TryParse(value,NumberStyles.Float,CultureInfo.InvariantCulture,out var n)||!double.IsFinite(n)||n<0||n>(key==ThemeKeys.Animation?1500:64)||(key==ThemeKeys.FontSize&&n<10))throw new System.IO.InvalidDataException("Некорректный размер темы.");
            }
            else if(!System.Text.RegularExpressions.Regex.IsMatch(value,@"\A#(?:[0-9a-fA-F]{6}|[0-9a-fA-F]{8})\z"))throw new System.IO.InvalidDataException("Некорректный цвет темы.");
        }
    }
    public static void Apply(IReadOnlyDictionary<string,string> tokens)
    {
        Validate(tokens);foreach(var (key,value) in tokens)
        {
            if(key==ThemeKeys.Radius){Application.Current.Resources[key]=new CornerRadius(double.Parse(value,CultureInfo.InvariantCulture));Application.Current.Resources["App.ButtonRadius"]=new CornerRadius(12);}
            else if(key is ThemeKeys.FontSize or ThemeKeys.Animation)Application.Current.Resources[key]=double.Parse(value,CultureInfo.InvariantCulture);
            else Application.Current.Resources[key]=new SolidColorBrush((Color)ColorConverter.ConvertFromString(value));
        }
    }
    public static void Reset(){Apply(Defaults);Application.Current.Resources[ThemeKeys.Font]=new FontFamily("Segoe UI Variable, Segoe UI");}
}
