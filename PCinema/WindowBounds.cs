using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace PCinema;

/// <summary>Keep borderless maximized windows inside the current monitor's work area.</summary>
internal static class WindowBounds
{
    public static void Attach(Window window, Func<bool> fullscreen)
    {
        var source=HwndSource.FromHwnd(new WindowInteropHelper(window).Handle);
        source?.AddHook((nint hwnd,int message,nint wParam,nint lParam,ref bool handled)=>
        {
            if(message!=0x0024||fullscreen())return 0; // WM_GETMINMAXINFO; coordinates are physical pixels.
            var monitor=MonitorFromWindow(hwnd,2);
            var info=new MonitorInfo{Size=Marshal.SizeOf<MonitorInfo>()};
            if(!GetMonitorInfo(monitor,ref info))return 0;
            var bounds=info.Work;
            var limits=Marshal.PtrToStructure<MinMaxInfo>(lParam);
            limits.MaxPosition=new(bounds.Left-info.Monitor.Left,bounds.Top-info.Monitor.Top);
            limits.MaxSize=new(bounds.Right-bounds.Left,bounds.Bottom-bounds.Top);
            Marshal.StructureToPtr(limits,lParam,false);
            handled=true;
            return 0;
        });
    }
    [StructLayout(LayoutKind.Sequential)] struct Point(int x,int y){public int X=x,Y=y;}
    [StructLayout(LayoutKind.Sequential)] struct Rect{public int Left,Top,Right,Bottom;}
    [StructLayout(LayoutKind.Sequential)] struct MinMaxInfo{public Point Reserved,MaxSize,MaxPosition,MinTrackSize,MaxTrackSize;}
    [StructLayout(LayoutKind.Sequential)] struct MonitorInfo{public int Size;public Rect Monitor,Work;public uint Flags;}
    [DllImport("user32.dll")] static extern nint MonitorFromWindow(nint hwnd,uint flags);
    [DllImport("user32.dll",CharSet=CharSet.Auto)] [return:MarshalAs(UnmanagedType.Bool)] static extern bool GetMonitorInfo(nint monitor,ref MonitorInfo info);
}
