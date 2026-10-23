using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;
namespace PCinema;

public sealed class LocalImageConverter : IValueConverter
{
    const long Limit=128L*1024*1024;
    static readonly object sync=new();
    static readonly Dictionary<string,(BitmapImage Image,long Bytes,LinkedListNode<string> Node)> cache=[];
    static readonly LinkedList<string> lru=new();
    static readonly SemaphoreSlim readers=new(2);
    static long bytes;
    public static async Task<BitmapImage?> LoadAsync(string? path,int width,CancellationToken token)
    {
        if(string.IsNullOrWhiteSpace(path))return null;
        await readers.WaitAsync(token);
        try{return await Task.Run(()=>{token.ThrowIfCancellationRequested();return Load(path,width);},token);}finally{readers.Release();}
    }
    public static BitmapImage? Load(string? path,int width=400)
    {
        if(string.IsNullOrWhiteSpace(path))return null;var key=path+"|"+width;
        lock(sync)if(cache.TryGetValue(key,out var hit)){lru.Remove(hit.Node);lru.AddLast(hit.Node);return hit.Image;}
        try
        {
            var image=new BitmapImage();image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;image.DecodePixelWidth=width;image.UriSource=new Uri(path);image.EndInit();image.Freeze();
            long size=(long)image.PixelWidth*image.PixelHeight*4;
            lock(sync)
            {
                if(cache.TryGetValue(key,out var hit))return hit.Image;
                while(bytes+size>Limit&&lru.First!=null){var oldest=lru.First.Value;bytes-=cache[oldest].Bytes;cache.Remove(oldest);lru.RemoveFirst();}
                if(size<=Limit){var node=lru.AddLast(key);cache.Add(key,(image,size,node));bytes+=size;}
            }
            return image;
        }catch(UnauthorizedAccessException){return null;}catch(ArgumentException){return null;}catch(IOException){return null;}catch(NotSupportedException){return null;}catch(FileFormatException){return null;}
    }
    public object? Convert(object value,Type type,object parameter,CultureInfo culture)=>Load(value as string,int.TryParse(parameter?.ToString(),out int width)?width:400);
    public object ConvertBack(object value,Type type,object parameter,CultureInfo culture)=>throw new NotSupportedException();
}
