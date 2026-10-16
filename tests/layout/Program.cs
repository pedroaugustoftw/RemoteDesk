using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using RemoteDesk.Desktop;
static class Program
{
    [STAThread] static int Main()
    {
        try
        {
            int count=0;
            foreach(var screen in new[]{(1280d,672d),(1366d,728d),(1920d,1040d),(1024d,720d)})
            foreach(double dpi in new[]{96d,120d,144d,192d})
            foreach(var desired in new[]{(1100d,720d),(1080d,720d),(680d,650d)})
            {
                var size=WindowSizing.Plan(screen.Item1,screen.Item2,dpi,desired.Item1,desired.Item2);
                if(size.Width*dpi/96>screen.Item1*.90+.01||size.Height*dpi/96>screen.Item2*.90+.01)throw new Exception("Planejamento excedeu a área útil.");count++;
            }
            var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};
            var window=new Window{Width=1400,Height=900,MinWidth=800,MinHeight=600};WindowSizing.Attach(window,1100,720);window.Show();
            window.Dispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);
            var handle=new WindowInteropHelper(window).Handle;GetWindowRect(handle,out var bounds);var info=new MonitorInfo{Size=Marshal.SizeOf<MonitorInfo>()};GetMonitorInfo(MonitorFromWindow(handle,2),ref info);
            if(bounds.Left<info.Work.Left||bounds.Top<info.Work.Top||bounds.Right>info.Work.Right||bounds.Bottom>info.Work.Bottom)throw new Exception("Janela real excedeu monitor.");
            window.Close();app.Shutdown();
            Console.WriteLine($"PASS {count} cenários calculados (100/125/150/200%); janela real dentro da área útil. Escalas alternativas simuladas, configuração do Windows preservada.");return 0;
        }
        catch(Exception ex){Console.WriteLine("FAIL "+ex);return 1;}
    }
    [StructLayout(LayoutKind.Sequential)]struct Rect{public int Left,Top,Right,Bottom;}
    [StructLayout(LayoutKind.Sequential)]struct MonitorInfo{public int Size;public Rect Monitor,Work;public uint Flags;}
    [DllImport("user32.dll")]static extern bool GetWindowRect(IntPtr window,out Rect rect);
    [DllImport("user32.dll")]static extern IntPtr MonitorFromWindow(IntPtr window,uint flags);
    [DllImport("user32.dll",CharSet=CharSet.Auto)]static extern bool GetMonitorInfo(IntPtr monitor,ref MonitorInfo info);
}
