using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace PCinema;

public static class AsyncImage
{
    public static readonly DependencyProperty PathProperty=DependencyProperty.RegisterAttached("Path",typeof(string),typeof(AsyncImage),new PropertyMetadata(null,Changed));
    public static readonly DependencyProperty WidthProperty=DependencyProperty.RegisterAttached("Width",typeof(int),typeof(AsyncImage),new PropertyMetadata(400,Changed));
    static readonly DependencyProperty RequestProperty=DependencyProperty.RegisterAttached("Request",typeof(Request),typeof(AsyncImage));
    sealed class Request{public CancellationTokenSource? Cancellation;}
    public static void SetPath(DependencyObject element,string? value)=>element.SetValue(PathProperty,value);
    public static string? GetPath(DependencyObject element)=>(string?)element.GetValue(PathProperty);
    public static void SetWidth(DependencyObject element,int value)=>element.SetValue(WidthProperty,value);
    public static int GetWidth(DependencyObject element)=>(int)element.GetValue(WidthProperty);
    public static Image Create(string? path,int width){var image=new Image();SetWidth(image,width);SetPath(image,path);return image;}
    static void Changed(DependencyObject element,DependencyPropertyChangedEventArgs args)
    {
        if(element is not FrameworkElement target)return;
        if(target.GetValue(RequestProperty) is not Request)
        {
            target.SetValue(RequestProperty,new Request());target.Loaded+=(_,_)=>Load(target);
            target.Unloaded+=(_,_)=>{var request=(Request)target.GetValue(RequestProperty);request.Cancellation?.Cancel();};
        }
        if(target.IsLoaded)Load(target);
    }
    static async void Load(FrameworkElement target)
    {
        var request=(Request)target.GetValue(RequestProperty);request.Cancellation?.Cancel();
        var cancellation=request.Cancellation=new CancellationTokenSource();
        if(target is Image image)image.Source=null;else if(target is Border border)border.Background=null;
        try
        {
            var bitmap=await LocalImageConverter.LoadAsync(GetPath(target),GetWidth(target),cancellation.Token);
            if(cancellation.IsCancellationRequested||!target.IsLoaded)return;
            if(target is Image view)view.Source=bitmap;else if(target is Border panel)panel.Background=bitmap==null?null:new ImageBrush(bitmap){Stretch=Stretch.UniformToFill};
        }catch(OperationCanceledException){}
        finally{if(ReferenceEquals(request.Cancellation,cancellation))request.Cancellation=null;cancellation.Dispose();}
    }
}
