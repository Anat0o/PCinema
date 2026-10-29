using System.Windows;
using PCinema.ModuleApi;
namespace PCinema.Modules;
// The built-in presentation uses the same view boundary as third-party skins.
// View state and all mutations remain in the application's services.
internal sealed class BuiltInSkin : IModuleSkin
{
    public FrameworkElement CreateShell(ShellContext context)=>context.Content;
    public FrameworkElement CreateView(ViewContext context)=>context.BuiltInView;
    public IReadOnlyDictionary<string,string> ThemeTokens=>ThemeValues.Defaults;
}
