using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using RemoteDesk.Desktop;
using RemoteDesk.Infrastructure;
using RemoteDesk.ServerSetup;
using Microsoft.Win32;

namespace RemoteDesk.Uninstall;
internal static class Program
{
    static bool Admin=>new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
    [STAThread] static int Main(string[] args)
    {
        bool registering=args.Contains("--register-client")||args.Contains("--register-server");
        bool server=args.Contains("--server")||args.Contains("--register-server");
        try
        {
            string executable=Environment.ProcessPath??throw new IOException("Executável não identificado.");
            if(server&&!Admin)
            {
                var elevate=new ProcessStartInfo(executable){UseShellExecute=true,Verb="runas"};elevate.ArgumentList.Add(registering?"--register-server":"--server");Process.Start(elevate);return 0;
            }
            if(registering)
            {
                string root=server?InstalledApplication.ServerRoot:InstalledApplication.ClientRoot;InstalledApplication.CheckOwned(root,server);
                string helper=InstalledApplication.Helper(server);InstalledApplication.CheckNoLinks(helper);Directory.CreateDirectory(Path.GetDirectoryName(helper)!);
                if(!Path.GetFullPath(executable).Equals(helper,StringComparison.OrdinalIgnoreCase))File.Copy(executable,helper,true);
                InstalledApplication.Register(server);return 0;
            }
            // Run the native helper outside the installation so its binaries can be removed.
            string temporaryRoot=server?Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"RemoteDesk Maintenance","Uninstall"):Path.Combine(Path.GetTempPath(),"RemoteDesk-Uninstall");
            bool staged=Guid.TryParseExact(Path.GetFileName(Path.GetDirectoryName(executable)),"N",out _)&&string.Equals(Path.GetDirectoryName(Path.GetDirectoryName(executable)),temporaryRoot,StringComparison.OrdinalIgnoreCase);
            if(!args.Contains("--worker")||!staged)
            {
                string stage=Path.Combine(temporaryRoot,Guid.NewGuid().ToString("N"));
                InstalledApplication.CheckNoLinks(stage);Directory.CreateDirectory(stage);
                if(server)
                {
                    var acl=new DirectorySecurity();acl.SetAccessRuleProtection(true,false);acl.SetOwner(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid,null));
                    foreach(var sid in new[]{WellKnownSidType.BuiltinAdministratorsSid,WellKnownSidType.LocalSystemSid})acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(sid,null),FileSystemRights.FullControl,InheritanceFlags.ContainerInherit|InheritanceFlags.ObjectInherit,PropagationFlags.None,AccessControlType.Allow));
                    new DirectoryInfo(temporaryRoot).SetAccessControl(acl);new DirectoryInfo(stage).SetAccessControl(acl);
                }
                string copy=Path.Combine(stage,"RemoteDesk.Uninstall.exe");File.Copy(executable,copy,false);
                var child=new ProcessStartInfo(copy){UseShellExecute=false,CreateNoWindow=true};child.ArgumentList.Add("--worker");if(server)child.ArgumentList.Add("--server");child.ArgumentList.Add("--parent");child.ArgumentList.Add(Environment.ProcessId.ToString());Process.Start(child);return 0;
            }
            int parentArg=Array.IndexOf(args,"--parent");if(parentArg>=0&&parentArg+1<args.Length&&int.TryParse(args[parentArg+1],out int parentId))
                try{using var parent=Process.GetProcessById(parentId);if(!parent.WaitForExit(15000))throw new IOException("Aguarde o fechamento do desinstalador anterior.");}catch(ArgumentException){}
            var app=new Application();string name=server?"RemoteDesk Server":"RemoteDesk";
            var window=new Window{Title=name+" — Desinstalar",Width=520,SizeToContent=SizeToContent.Height,ResizeMode=ResizeMode.NoResize,WindowStartupLocation=WindowStartupLocation.CenterScreen,Background=new SolidColorBrush(Color.FromRgb(23,23,23)),Foreground=Brushes.White,FontFamily=new FontFamily("Segoe UI"),FontSize=14};
            var panel=new StackPanel{Margin=new Thickness(24)};window.Content=panel;
            panel.Children.Add(new TextBlock{Text="Desinstalar "+name,FontSize=23,FontWeight=FontWeights.SemiBold});
            panel.Children.Add(new TextBlock{Text=server?"O serviço será parado e removido, junto com os arquivos do programa e suas regras de firewall. Certificado, configuração e cadastro de dispositivos serão preservados.":"Os arquivos do aplicativo, atalhos e inicialização automática serão removidos. Seu ID, configurações e dispositivos salvos serão preservados.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,18,0,18)});
            var status=new TextBlock{TextWrapping=TextWrapping.Wrap};panel.Children.Add(status);
            var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,18,0,0)};panel.Children.Add(buttons);
            bool busy=false,done=false;var cancel=new Button{Content="Cancelar",Padding=new Thickness(16,8,16,8),Margin=new Thickness(0,0,8,0)};cancel.Click+=(_,_)=>window.Close();buttons.Children.Add(cancel);
            var remove=new Button{Content="Desinstalar",Padding=new Thickness(16,8,16,8),Background=new SolidColorBrush(Color.FromRgb(29,111,242)),Foreground=Brushes.White};buttons.Children.Add(remove);
            remove.Click+=async(_,_)=>{
                if(done){window.Close();return;}busy=true;remove.IsEnabled=cancel.IsEnabled=false;status.Text="Removendo o programa…";
                try{await Task.Run(()=>Remove(server));status.Text="Desinstalação concluída. Os dados de configuração foram preservados.";done=true;remove.Content="Concluir";}
                catch(Exception ex){status.Text=ex.Message;}
                finally{busy=false;remove.IsEnabled=cancel.IsEnabled=true;}
            };
            window.Closing+=(_,e)=>{if(busy)e.Cancel=true;};app.Run(window);
            if(server){MoveFileEx(executable,null,4);MoveFileEx(Path.GetDirectoryName(executable)!,null,4);}return 0;
        }
        catch(Exception ex){MessageBox.Show(ex.Message,"RemoteDesk — Desinstalação",MessageBoxButton.OK,MessageBoxImage.Error);return 1;}
    }
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool MoveFileEx(string existing,string? destination,uint flags);
    static void Remove(bool server)
    {
        string root=server?InstalledApplication.ServerRoot:InstalledApplication.ClientRoot;
        if(Directory.Exists(root))InstalledApplication.CheckOwned(root,server);
        if(server)
        {
            if(ServiceManager.Exists())
            {
                using var key=Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\RemoteDeskServer");
                string image=(key?.GetValue("ImagePath") as string??"").Trim().Trim('"');
                if(!image.Equals(Path.Combine(root,"service","RemoteDesk.Service.exe"),StringComparison.OrdinalIgnoreCase))throw new IOException("O serviço não corresponde a esta instalação. Remoção cancelada.");
                ServiceManager.Stop();var until=DateTime.UtcNow.AddSeconds(40);while(ServiceStateReader.Read().Code!=1&&DateTime.UtcNow<until)Thread.Sleep(250);
                if(ServiceStateReader.Read().Code!=1)throw new IOException("O serviço não parou. Tente novamente após encerrar as sessões.");ServiceManager.Remove();
            }
            RemoveFirewall(root);
        }
        else
        {
            foreach(var process in Process.GetProcessesByName("RemoteDesk"))
                using(process){string? path;try{path=process.MainModule?.FileName;}catch{throw new IOException("Feche o RemoteDesk antes de desinstalar.");}if(path?.Equals(Path.Combine(root,"RemoteDesk.exe"),StringComparison.OrdinalIgnoreCase)==true)throw new IOException("Feche o RemoteDesk, incluindo o ícone próximo ao relógio, e tente novamente.");}
        }
        // Delete only the fixed product directory after checking its ownership marker and links.
        string parent=Path.GetDirectoryName(root)!;
        var patterns=server?new[]{"RemoteDesk Server.previous-*","RemoteDesk Server.failed-*","RemoteDesk Server.stage-*"}:new[]{".previous-*",".install-*"};
        foreach(string pattern in patterns)foreach(string backup in Directory.EnumerateDirectories(parent,pattern,SearchOption.TopDirectoryOnly))
        {
            string marker=Path.Combine(backup,server?InstalledApplication.ServerMarker:InstalledApplication.ClientMarker);
            if(File.Exists(marker)&&File.ReadAllText(marker)==(server?"RemoteDesk Server":"RemoteDesk managed application directory v1"))InstalledApplication.DeleteOwned(backup,server);
        }
        if(Directory.Exists(root))InstalledApplication.DeleteOwned(root,server);
        if(!server)
        {
            using var run=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run",true);run?.DeleteValue("RemoteDesk",false);
            foreach(string shortcut in new[]{Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),"RemoteDesk.lnk"),Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs),"RemoteDesk","RemoteDesk.lnk")})if(File.Exists(shortcut))File.Delete(shortcut);
        }
        InstalledApplication.Unregister(server);
    }
    static void RemoveFirewall(string root)
    {
        dynamic policy=Activator.CreateInstance(Type.GetTypeFromProgID("HNetCfg.FwPolicy2")!)!;
        try
        {
            foreach(string component in new[]{"Server","Relay"})
            {
                dynamic? rule=null;
                try{rule=policy.Rules.Item("RemoteDesk-LAN-"+component);string expected=Path.Combine(root,component.ToLowerInvariant(),"RemoteDesk."+component+".exe");if(((string)rule.ApplicationName).Equals(expected,StringComparison.OrdinalIgnoreCase))policy.Rules.Remove("RemoteDesk-LAN-"+component);}
                catch(Exception ex)when(ex.HResult==unchecked((int)0x80070002)){}
                finally{if(rule is not null)Marshal.FinalReleaseComObject(rule);}
            }
        }
        finally{Marshal.FinalReleaseComObject(policy);}
    }
}
