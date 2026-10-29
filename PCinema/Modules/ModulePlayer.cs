using System.Windows;
using PCinema.ModuleApi;
namespace PCinema;
public partial class PlayerWindow
{
    FrameworkElement? moduleControls;
    void InstallModuleControls()
    {
        if(Host.Modules.Skin==null)return;
        int index=Overlay.Children.IndexOf(Controls);if(index<0)index=Math.Min(1,Overlay.Children.Count);Overlay.Children.Remove(Controls);
        moduleControls=Host.Modules.CreateView(new(ViewKind.Player,Controls,Host.Modules.Context(Host.Modules.SkinOwner!),series.Id));
        Overlay.Children.Insert(index,moduleControls);
    }
    void SetModuleCompact(bool compact)
    {
        if(moduleControls==null||ReferenceEquals(moduleControls,Controls))return;
        if(compact){MainWindow.DetachModuleView(moduleControls);MainWindow.DetachModuleView(Controls);Controls.Visibility=Visibility.Visible;Overlay.Children.Add(Controls);}
        else {Overlay.Children.Remove(Controls);moduleControls=null;InstallModuleControls();}
    }
    public void ModuleSetPause(bool value)=>SetPaused(value);
    public void ModuleSetPosition(long milliseconds){if(!loading&&state.Seekable)_=seek.RequestAsync(Math.Clamp(milliseconds,0,Math.Max(0,state.Length)),true);}
    public void ModuleShowOptions()=>ShowTracks(this,new());
    public void ModuleNext()=>Next(this,new());
    public void ModuleSetVolume(int volume)=>VolumeSlider.Value=volume;
}
