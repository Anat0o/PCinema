using System.Windows;
namespace PCinema;

public class StyledWindow : Window
{
    public StyledWindow() { Style = (Style)Application.Current.Resources[typeof(Window)]; }
}
