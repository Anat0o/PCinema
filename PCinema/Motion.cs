using System.Windows;
namespace PCinema;

public static class Motion
{
    public static Duration Hover=>new(TimeSpan.FromMilliseconds(SystemParameters.ClientAreaAnimation?160:0));
    public static Duration Press=>new(TimeSpan.FromMilliseconds(SystemParameters.ClientAreaAnimation?120:0));
}
