using System;
using System.IO;
using System.Windows;
using Forms=System.Windows.Forms;
namespace RemoteDesk.Client.UI;
public partial class MainWindow
{
    private Forms.NotifyIcon? tray;
    private void InitializeTray()
    {
        var args=Environment.GetCommandLineArgs();
        if(Array.Exists(args,a=>a.StartsWith("--render")||a=="--ui-test"||a=="--connection-test"))return;
        using var resource=Application.GetResourceStream(new Uri("pack://application:,,,/UI/RemoteDesk.ico"))!.Stream;
        tray=new Forms.NotifyIcon{Text="RemoteDesk — aguardando solicitações",Icon=new System.Drawing.Icon(resource),Visible=true};
        var menu=new Forms.ContextMenuStrip();menu.Items.Add("Abrir RemoteDesk",null,(_,_)=>RestoreFromTray());
        menu.Items.Add("Iniciar com o Windows",null,(_,_)=>{
            try{Services.StartupRegistration.Set(!Services.StartupRegistration.Enabled,Environment.ProcessPath!);}
            catch(Exception ex){MessageBox.Show(this,ex.Message,"RemoteDesk");}
        });
        menu.Opening+=(_,_)=>((Forms.ToolStripMenuItem)menu.Items[1]).Checked=Services.StartupRegistration.Enabled;
        menu.Items.Add("Sair do RemoteDesk",null,(_,_)=>{exiting=true;Close();});tray.ContextMenuStrip=menu;
        tray.DoubleClick+=(_,_)=>Dispatcher.Invoke(RestoreFromTray);
        StateChanged+=(_,_)=>{if(WindowState==WindowState.Minimized)Hide();};
        bool minimizedOnce=false;
        if(Array.Exists(args,a=>a=="--minimized"))Loaded+=(_,_)=>{if(minimizedOnce)return;minimizedOnce=true;WindowState=WindowState.Minimized;Hide();};
        Closed+=(_,_)=>{tray.Visible=false;tray.Dispose();menu.Dispose();};
    }
    public void RestoreFromTray(){Show();WindowState=WindowState.Normal;Activate();}
    private async System.Threading.Tasks.Task CheckTray()
    {
        try
        {
            if(tray?.Visible!=true)throw new InvalidOperationException("Ícone da bandeja ausente.");
            WindowState=WindowState.Minimized;await System.Windows.Threading.Dispatcher.Yield();
            if(IsVisible)throw new InvalidOperationException("Janela não foi recolhida.");
            RestoreFromTray();await System.Windows.Threading.Dispatcher.Yield();
            if(!IsVisible||WindowState!=WindowState.Normal)throw new InvalidOperationException("Restauração falhou.");
            File.WriteAllText(Path.Combine(Services.Profile.DirectoryPath,"tray-test.txt"),"PASS ícone visível; minimizar recolhe; restaurar exibe; saída explícita encerra. Registro de inicialização e reinício Windows não exercitados.");
            exiting=true;Close();
        }
        catch(Exception ex){File.WriteAllText(Path.Combine(Services.Profile.DirectoryPath,"tray-test.txt"),"FAIL "+ex);Application.Current.Shutdown(1);}
    }
}
