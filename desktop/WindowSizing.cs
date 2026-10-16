using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
namespace RemoteDesk.Desktop;
public static class WindowSizing
{
    public static Size Plan(double workWidthPixels,double workHeightPixels,double dpi,double preferredWidth,double preferredHeight)
    {
        if(dpi<=0||workWidthPixels<=0||workHeightPixels<=0)throw new ArgumentOutOfRangeException(nameof(dpi));
        double scale=dpi/96;
        return new Size(Math.Min(preferredWidth,workWidthPixels/scale*.90),Math.Min(preferredHeight,workHeightPixels/scale*.90));
    }
    public static void Attach(Window window,double preferredWidth,double preferredHeight)
    {
        bool fitting=false;
        void Fit(bool initial)
        {
            if(fitting||window.WindowState!=WindowState.Normal)return;
            var handle=new WindowInteropHelper(window).Handle;if(handle==IntPtr.Zero)return;
            var info=new MonitorInfo{Size=Marshal.SizeOf<MonitorInfo>()};if(!GetMonitorInfo(MonitorFromWindow(handle,2),ref info))return;
            fitting=true;
            try
            {
                double dpi=GetDpiForWindow(handle);if(dpi<=0)dpi=96;
                double scale=dpi/96;double width=info.Work.Right-info.Work.Left,height=info.Work.Bottom-info.Work.Top;
                var planned=Plan(width,height,dpi,initial?preferredWidth:window.ActualWidth,initial?preferredHeight:window.ActualHeight);
                window.MinWidth=Math.Min(window.MinWidth,planned.Width);window.MinHeight=Math.Min(window.MinHeight,planned.Height);
                window.Width=planned.Width;window.Height=planned.Height;
                int w=(int)Math.Round(planned.Width*scale),h=(int)Math.Round(planned.Height*scale);
                GetWindowRect(handle,out var bounds);
                int x=initial?info.Work.Left+((int)width-w)/2:Math.Clamp(bounds.Left,info.Work.Left,info.Work.Right-w);
                int y=initial?info.Work.Top+((int)height-h)/2:Math.Clamp(bounds.Top,info.Work.Top,info.Work.Bottom-h);
                SetWindowPos(handle,IntPtr.Zero,x,y,w,h,0x14);
            }
            finally{fitting=false;}
        }
        HwndSource? source=null;
        IntPtr Watch(IntPtr handle,int message,IntPtr wParam,IntPtr lParam,ref bool handled)
        {
            if(message is 0x007E or 0x001A)window.Dispatcher.BeginInvoke(DispatcherPriority.Loaded,new Action(()=>Fit(false)));
            return IntPtr.Zero;
        }
        window.SourceInitialized+=(_,_)=>{Fit(true);source=HwndSource.FromHwnd(new WindowInteropHelper(window).Handle);source?.AddHook(Watch);};
        window.Closed+=(_,_)=>source?.RemoveHook(Watch);
        window.DpiChanged+=(_,_)=>window.Dispatcher.BeginInvoke(DispatcherPriority.Loaded,new Action(()=>Fit(false)));
        window.StateChanged+=(_,_)=>{if(window.WindowState==WindowState.Normal)window.Dispatcher.BeginInvoke(new Action(()=>Fit(false)));};
    }
    [StructLayout(LayoutKind.Sequential)]struct Rect{public int Left,Top,Right,Bottom;}
    [StructLayout(LayoutKind.Sequential)]struct MonitorInfo{public int Size;public Rect Monitor,Work;public uint Flags;}
    [DllImport("user32.dll")]static extern IntPtr MonitorFromWindow(IntPtr window,uint flags);
    [DllImport("user32.dll",CharSet=CharSet.Auto)]static extern bool GetMonitorInfo(IntPtr monitor,ref MonitorInfo info);
    [DllImport("user32.dll")]static extern uint GetDpiForWindow(IntPtr window);
    [DllImport("user32.dll")]static extern bool GetWindowRect(IntPtr window,out Rect rect);
    [DllImport("user32.dll")]static extern bool SetWindowPos(IntPtr window,IntPtr after,int x,int y,int width,int height,uint flags);
}
