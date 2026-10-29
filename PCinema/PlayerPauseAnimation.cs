using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
namespace PCinema;

public partial class PlayerWindow
{
    bool pauseInfoShown;
    int pauseAnimationVersion;
    string? pauseArtworkKey;
    CancellationTokenSource? pauseArtworkCancellation;
    async Task LoadPauseArtwork()
    {
        var key=series.Poster+"|"+series.Backdrop;
        if(pauseArtworkKey==key||closing)return;
        pauseArtworkKey=key;pauseArtworkCancellation?.Cancel();
        var cancellation=pauseArtworkCancellation=new CancellationTokenSource();
        try
        {
            var image=await LocalImageConverter.LoadAsync(series.Poster,1400,cancellation.Token)
                ??await LocalImageConverter.LoadAsync(series.Backdrop,1400,cancellation.Token);
            if(closing||cancellation.IsCancellationRequested)return;
            // Assign the actual drawing source; no ElementName binding across VideoView's window.
            PauseArt.Source=image;
            PauseArt.BeginAnimation(OpacityProperty,new DoubleAnimation(0,1,TimeSpan.FromMilliseconds(SystemParameters.ClientAreaAnimation?350:0)));
        }
        catch(OperationCanceledException){}
        catch(Exception ex){PerformanceLog.Write("pause.artwork.failed",error:ex);}
        finally{if(ReferenceEquals(pauseArtworkCancellation,cancellation))pauseArtworkCancellation=null;cancellation.Dispose();}
    }
    void AnimatePause(bool show)
    {
        if(pauseInfoShown==show)return;
        pauseInfoShown=show;int version=++pauseAnimationVersion;
        bool wasHidden=PauseInfo.Visibility!=Visibility.Visible;
        if(show)PauseInfo.Visibility=Visibility.Visible;
        var duration=TimeSpan.FromMilliseconds(SystemParameters.ClientAreaAnimation?(show?350:220):0);
        var easing=new SineEase{EasingMode=EasingMode.EaseInOut};
        double target=show?1:0;
        var opacity=new DoubleAnimation(PauseInfo.Opacity,target,duration){EasingFunction=easing};
        opacity.Completed+=(_,_)=>{if(version==pauseAnimationVersion&&!pauseInfoShown)PauseInfo.Visibility=Visibility.Collapsed;};
        PauseInfo.BeginAnimation(OpacityProperty,opacity);
        // Animate the cached poster layer without repainting it every frame.
        PauseArtOffset.BeginAnimation(TranslateTransform.XProperty,new DoubleAnimation(wasHidden?-36:PauseArtOffset.X,show?0:-36,duration){EasingFunction=easing});
        var copy=new DoubleAnimation(wasHidden?-24:PauseCopyOffset.X,show?0:-24,TimeSpan.FromMilliseconds(SystemParameters.ClientAreaAnimation?(show?350:220):0)){EasingFunction=easing};
        if(show&&wasHidden&&SystemParameters.ClientAreaAnimation)copy.BeginTime=TimeSpan.FromMilliseconds(0);
        PauseCopyOffset.BeginAnimation(TranslateTransform.XProperty,copy);
    }
}
