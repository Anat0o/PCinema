using System.Globalization;
using System.Collections.Concurrent;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;
namespace PCinema;

public sealed class LocalImageConverter : IValueConverter
{
    static readonly ConcurrentDictionary<string, WeakReference<BitmapImage>> cache = [];
    public static BitmapImage? Load(string? path, int width = 400)
    {
        if (string.IsNullOrWhiteSpace(path)) return null; var key = path + "|" + width;
        if(cache.Count > 512) foreach(var item in cache.Where(x => !x.Value.TryGetTarget(out _)).Take(256)) cache.TryRemove(item.Key,out _);
        if (cache.TryGetValue(key, out var reference) && reference.TryGetTarget(out var image)) return image;
        try { image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.DecodePixelWidth = width; image.UriSource = new Uri(path); image.EndInit(); image.Freeze(); cache[key] = new(image); return image; } catch (IOException) { return null; } catch (NotSupportedException) { return null; } catch (System.IO.FileFormatException) { return null; }
    }
    public object? Convert(object value, Type type, object parameter, CultureInfo culture) => Load(value as string, int.TryParse(parameter?.ToString(), out int width) ? width : 400);
    public object ConvertBack(object value, Type type, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

