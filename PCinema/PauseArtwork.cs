using System.Windows;
using System.Windows.Media;
namespace PCinema;

public sealed class PauseArtwork : FrameworkElement
{
    public static readonly DependencyProperty SourceProperty=DependencyProperty.Register(nameof(Source),typeof(ImageSource),typeof(PauseArtwork),new FrameworkPropertyMetadata(null,FrameworkPropertyMetadataOptions.AffectsRender));
    public ImageSource? Source{get=>(ImageSource?)GetValue(SourceProperty);set=>SetValue(SourceProperty,value);}
    readonly LinearGradientBrush fade=new(new GradientStopCollection{new(Colors.Transparent,0),new(Colors.White,.08),new(Colors.White,.68),new(Colors.Transparent,1)},new Point(0,0),new Point(1,0));
    public PauseArtwork()=>fade.Freeze();
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        double w=ActualWidth,h=ActualHeight;
        if(w<=0||h<=0||Source==null||Source.Width<=0||Source.Height<=0)return;
        // Fit the whole poster inside the left area instead of cropping it to cover the screen.
        double scale=Math.Min(w*.62/Source.Width,h*.84/Source.Height);
        double width=Source.Width*scale,height=Source.Height*scale;
        var imageRect=new Rect(w*.06,(h-height)/2,width,height);
        dc.PushOpacityMask(fade);dc.DrawImage(Source,imageRect);dc.Pop();
    }
}
