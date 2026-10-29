using System.Windows;
namespace PCinema;

public partial class MainWindow
{
    TrayIcon? tray;
    bool exitRequested,closeToTray;
    readonly SerialWorkQueue preferenceWrites=new();
    public bool CloseToTray=>closeToTray;
    public bool IsPictureInPicture{get;private set;}
    Rect pipBounds;WindowState pipState;bool pipTopmost;double pipMinWidth,pipMinHeight;
    public async Task SetCloseToTray(bool enabled)
    {
        closeToTray=enabled;
        try{await preferenceWrites.Run(()=>store.Setting("close_to_tray",enabled?"true":"false"));}
        catch(Exception ex){PerformanceLog.Write("tray.preference.failed",error:ex);await Notice("Не удалось сохранить настройку трея.");}
    }
    bool HideToTray()
    {
        try
        {
            tray??=new TrayIcon(()=>Dispatcher.BeginInvoke(RestoreFromTray),()=>Dispatcher.BeginInvoke(()=>OpenTrayPage(false)),()=>Dispatcher.BeginInvoke(()=>OpenTrayPage(true)),()=>Dispatcher.BeginInvoke(ExitFromTray));
            tray.Visible=true;if(current is PlayerWindow player)player.PauseForTray();Hide();return true;
        }
        catch(Exception ex){PerformanceLog.Write("tray.failed",error:ex);return false;}
    }
    public void RestoreFromTray()
    {
        if(shutdownPending||closed)return;
        Show();if(WindowState==WindowState.Minimized)WindowState=WindowState.Normal;Activate();
        if(tray!=null)tray.Visible=false;
    }
    void OpenTrayPage(bool showHistory)
    {
        RestoreFromTray();if(shutdownPending||closed)return;
        cancelModal?.Invoke();ExitPictureInPicture();if(IsFullscreen)ToggleFullscreen();
        current?.Leave();history.Clear();current=showHistory?new HistoryScreen(this,store):new LibraryScreen(this,store);Present();_=ReloadLibrary();
    }
    void ExitFromTray(){exitRequested=true;RestoreFromTray();Close();}
    public void TogglePictureInPicture()
    {
        if(current is not PlayerWindow player)return;
        if(IsPictureInPicture){ExitPictureInPicture();return;}
        if(IsFullscreen)ToggleFullscreen();
        pipBounds=WindowState==WindowState.Normal?new Rect(Left,Top,ActualWidth,ActualHeight):RestoreBounds;pipState=WindowState;pipTopmost=Topmost;pipMinWidth=MinWidth;pipMinHeight=MinHeight;
        WindowState=WindowState.Normal;MinWidth=340;MinHeight=240;IsPictureInPicture=true;Topmost=true;
        var area=SystemParameters.WorkArea;Width=480;Height=302;Left=area.Right-Width-24;Top=area.Bottom-Height-24;
        player.SetCompact(true);
    }
    public void ExitPictureInPicture()
    {
        if(!IsPictureInPicture)return;IsPictureInPicture=false;WindowState=WindowState.Normal;MinWidth=pipMinWidth;MinHeight=pipMinHeight;Topmost=pipTopmost;
        if(!pipBounds.IsEmpty){Left=pipBounds.Left;Top=pipBounds.Top;Width=pipBounds.Width;Height=pipBounds.Height;}WindowState=pipState;
        if(current is PlayerWindow player)player.SetCompact(false);
    }
}
