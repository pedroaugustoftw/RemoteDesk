using System.Windows;
namespace RemoteDesk.Client;
public partial class App : System.Windows.Application
{
    private Mutex? instance;
    private EventWaitHandle? activation;
    private RegisteredWaitHandle? activationWait;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        int parentArgument=Array.IndexOf(e.Args,"--wait-for-parent");
        if(parentArgument>=0&&parentArgument+1<e.Args.Length&&int.TryParse(e.Args[parentArgument+1],out int parentId)&&parentId!=Environment.ProcessId)
        {try{using var parent=System.Diagnostics.Process.GetProcessById(parentId);if(!parent.WaitForExit(15000)){Shutdown(1);return;}}catch(ArgumentException){}}
        if(e.Args.Contains("--island-test")||e.Args.Contains("--render-island")){ShutdownMode=ShutdownMode.OnExplicitShutdown;_=CheckIsland(e.Args.Contains("--render-island"));return;}
        if(e.Args.Contains("--files-test")||e.Args.Contains("--render-files")){ShutdownMode=ShutdownMode.OnExplicitShutdown;_=CheckFiles(e.Args.Contains("--render-files"));return;}
        if(e.Args.Contains("--connection-test")){ShutdownMode=ShutdownMode.OnExplicitShutdown;_=ConnectionTest();return;}
        if(!e.Args.Any(a=>a.StartsWith("--render")||a.EndsWith("-test")))
        {
            string suffix=System.Security.Principal.WindowsIdentity.GetCurrent().User!.Value;
            instance=new Mutex(true,@"Local\RemoteDesk-"+suffix,out bool first);
            activation=new EventWaitHandle(false,EventResetMode.AutoReset,@"Local\RemoteDesk-Activate-"+suffix);
            if(!first){activation.Set();Shutdown();return;}
            activationWait=ThreadPool.RegisterWaitForSingleObject(activation,(_,_)=>Dispatcher.BeginInvoke(()=>{if(MainWindow is UI.MainWindow window)window.RestoreFromTray();}),null,Timeout.Infinite,false);
        }
        DispatcherUnhandledException += (_, a) => { Services.Profile.Log(a.Exception.ToString()); if (Environment.GetCommandLineArgs().Any(v=>v.StartsWith("--render"))) Shutdown(1); else System.Windows.MessageBox.Show(a.Exception.Message, "RemoteDesk"); a.Handled = true; };
        var main = new UI.MainWindow(); MainWindow = main; main.Show();
    }
    protected override void OnExit(ExitEventArgs e){activationWait?.Unregister(null);activation?.Dispose();instance?.Dispose();base.OnExit(e);}
    private async Task ConnectionTest()
    {
        try{var profile=Services.Profile.Load();await using var connection=new Services.Connection();string? id=null;connection.Message+=m=>{if(m.Type=="ready")id=m.DeviceId;};await connection.Connect(profile);if(string.IsNullOrEmpty(id))throw new InvalidOperationException("Servidor não forneceu ID.");profile.DeviceId=id;profile.Save();System.IO.File.WriteAllText(System.IO.Path.Combine(Services.Profile.DirectoryPath,"connection-test.txt"),"PASS servidor conectado; ID: "+id);Shutdown(0);}
        catch(Exception ex){System.IO.File.WriteAllText(System.IO.Path.Combine(Services.Profile.DirectoryPath,"connection-test.txt"),"FAIL "+ex.Message);Shutdown(1);}
    }
}
