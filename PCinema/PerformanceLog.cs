using System.Diagnostics;
using System.IO;
namespace PCinema;

public static class PerformanceLog
{
    static readonly object sync=new();
    public static void Write(string operation,double milliseconds=0,Exception? error=null)
    {
        // Callers pass constant operation names, never paths, media titles or exception messages.
        lock(sync)try
        {
            var dir=Path.Combine(LibraryStore.DataDirectory,"diagnostics");Directory.CreateDirectory(dir);
            var file=Path.Combine(dir,"performance.log");
            if(File.Exists(file)&&new FileInfo(file).Length>=2*1024*1024)
            {if(File.Exists(file+".1"))File.Move(file+".1",file+".2",true);File.Move(file,file+".1",true);}
            File.AppendAllText(file,$"{DateTime.UtcNow:O} {operation} ms={milliseconds:F1} error={error?.GetType().Name??"none"}\n");
        }catch(IOException){}catch(UnauthorizedAccessException){}
    }
    public static T Measure<T>(string operation,Func<T> action)
    {var timer=Stopwatch.StartNew();try{return action();}finally{Write(operation,timer.Elapsed.TotalMilliseconds);}}
}
