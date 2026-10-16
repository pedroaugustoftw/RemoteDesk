using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Principal;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using RemoteDesk.Infrastructure;
using RemoteDesk.Setup;
namespace RemoteDesk.ServerSetup;
public partial class SetupWindow : Window
{
    bool busy;
    readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromSeconds(2)};
    public SetupWindow()
    {
        InitializeComponent();RemoteDesk.Desktop.WindowSizing.Attach(this,1080,720);
        try{IpField.Text=Public()?.Ip??DefaultIp();}catch(Exception ex){StatusText.Text=ex.Message;}
        var network=Public();NetworkMode.SelectedIndex=network?.NetworkMode=="vps"?1:0;UpdateModeFields();
        PublicServerField.Text=network?.PublicServerUri??"";PublicRelayField.Text=network?.PublicRelayUri??"";TunnelPortField.Text=(network?.TunnelPort??2750).ToString();
        Refresh();timer.Tick+=(_,_)=>{if(!busy)Refresh();};timer.Start();Closed+=(_,_)=>timer.Stop();
        SizeChanged+=(_,_)=>{bool stacked=ActualWidth<950;SidebarColumn.Width=new GridLength(215);Grid.SetColumnSpan(ServerCard,stacked?3:1);Grid.SetColumn(RelayCard,stacked?0:2);Grid.SetRow(RelayCard,stacked?1:0);Grid.SetColumnSpan(RelayCard,stacked?3:1);};
        Closing+=(_,e)=>{if(busy){e.Cancel=true;StatusText.Text="Aguarde a conclusão da operação para fechar esta janela.";}};
    }
    internal void ShowNetworkPreview()
    {
        NetworkMode.SelectedIndex=1;PublicServerField.Text="wss://remote.example.com/ws";PublicRelayField.Text="wss://remote.example.com/relay/ws";
        if(PublicFields.Visibility!=Visibility.Visible||PublicServerField.IsReadOnly||PublicRelayField.IsReadOnly)throw new InvalidOperationException("Public network fields are not editable.");
    }
    internal async Task CheckInterface()
    {
        timer.Stop();
        foreach(bool small in new[]{false,true})
        {
            Width=small?680:1080;Height=small?560:720;
            foreach(int mode in new[]{0,1,0})
            {
                NetworkMode.SelectedIndex=mode;UpdateModeFields();UpdateLayout();
                if((PublicFields.Visibility==Visibility.Visible)!=(mode==1)||(LocalFields.Visibility==Visibility.Visible)!=(mode==0)||(EndpointGrid.Visibility==Visibility.Visible)!=(mode==0))throw new Exception("Mode fields visibility mismatch.");
                foreach(int code in new[]{0,1,2,3,4,-1})
                {
                    ApplyServiceState(code);UpdateLayout();
                    if((StartButton.Visibility==Visibility.Visible)!=(code==1)||(StopButton.Visibility==Visibility.Visible)!=(code==4))throw new Exception("Service controls do not match Windows state.");
                    ContentScroll.ScrollToBottom();await System.Windows.Threading.Dispatcher.Yield();UpdateLayout();
                    var bounds=ServiceCard.TransformToAncestor((Visual)Content).TransformBounds(new Rect(ServiceCard.RenderSize));
                    if(bounds.Top<0||bounds.Bottom>((FrameworkElement)Content).ActualHeight)throw new Exception("Service controls outside window.");
                    var scrollBounds=ContentScroll.TransformToAncestor((Visual)Content).TransformBounds(new Rect(ContentScroll.RenderSize));
                    if(bounds.Bottom>scrollBounds.Top+1)throw new Exception("Service controls are inside scrolling page.");
                }
            }
        }
        Console.WriteLine("PASS local/public visibility; start/stop controls for six states; fixed toolbar at normal and compact sizes.");
    }
    internal void ShowDetails(){ContentScroll.ScrollToBottom();ContentScroll.UpdateLayout();}
    internal void SetIp(string value)=>IpField.Text=value;
    void InstallationPage_Click(object sender,RoutedEventArgs e)=>ContentScroll.ScrollToTop();
    void StatusPage_Click(object sender,RoutedEventArgs e)=>ContentScroll.ScrollToTop();
    static void WaitState(int expected)
    {
        var deadline=DateTime.UtcNow.AddSeconds(40);
        while(DateTime.UtcNow<deadline){if(ServiceStateReader.Read().Code==expected)return;System.Threading.Thread.Sleep(250);}
        throw new IOException("O serviço não atingiu o estado esperado; confira os logs.");
    }
    void NetworkPage_Click(object sender,RoutedEventArgs e)=>NetworkCard.BringIntoView();
    void NetworkMode_Changed(object sender,SelectionChangedEventArgs e)
    {
        UpdateModeFields();
    }
    void UpdateModeFields()
    {
        if(PublicFields==null||LocalFields==null||EndpointGrid==null)return;
        bool internet=NetworkMode.SelectedIndex==1;
        PublicFields.Visibility=internet?Visibility.Visible:Visibility.Collapsed;
        LocalFields.Visibility=internet?Visibility.Collapsed:Visibility.Visible;
        EndpointGrid.Visibility=internet?Visibility.Collapsed:Visibility.Visible;
        LocalHintCard.Visibility=internet?Visibility.Collapsed:Visibility.Visible;
    }
    internal void ApplyServiceState(int code)
    {
        bool installed=code>0,stable=code is 0 or 1 or 4;
        InstallButton.Content=installed?"Atualizar componentes":"Instalar e iniciar";
        InstallButton.IsEnabled=!busy&&stable;
        StartButton.Visibility=code==1?Visibility.Visible:Visibility.Collapsed;
        StopButton.Visibility=code==4?Visibility.Visible:Visibility.Collapsed;
        StartButton.IsEnabled=StopButton.IsEnabled=!busy;
        RemoveButton.Visibility=installed?Visibility.Visible:Visibility.Collapsed;
        RemoveButton.IsEnabled=!busy&&stable;
        IpField.IsEnabled=!busy&&!installed;
        SaveNetworkButton.Visibility=installed?Visibility.Visible:Visibility.Collapsed;
        NetworkSaveHint.Text=installed?"Aplicar salva o modo escolhido. Se estiver em execução, o servidor será reiniciado e as sessões serão encerradas.":"Preencha o modo escolhido e clique em Instalar e iniciar acima. A configuração será aplicada na instalação.";
        ServiceSummary.Text=code switch{0=>"Servidor não instalado",1=>"Servidor instalado • parado",4=>"Servidor instalado • em execução",2=>"Servidor iniciando…",3=>"Servidor parando…",_=>"Estado do serviço indisponível"};
        ServiceActionHint.Text=code switch{0=>"Instale o serviço para poder iniciar ou parar. Funciona nos dois modos.",1=>"Clique em Iniciar servidor. O serviço também inicia com o Windows.",4=>"O servidor já está ligado. Para desligá-lo, clique em Parar servidor.",2 or 3=>"Aguarde a conclusão. Os controles serão atualizados automaticamente.",_=>"Não foi possível consultar o Windows. Atualize o estado ou reabra como administrador."};
    }
    async void SaveNetwork_Click(object sender,RoutedEventArgs e)
    {
        string mode=NetworkMode.SelectedIndex==1?"vps":"local",server=PublicServerField.Text.Trim(),relay=PublicRelayField.Text.Trim();
        if(!int.TryParse(TunnelPortField.Text,out int port)){StatusText.Text="Informe uma porta válida.";return;}
        if(!Admin){StatusText.Text="Abra este instalador como administrador para salvar a rede.";return;}
        if(ServiceStateReader.Read().Code==4&&MessageBox.Show(this,"Salvar a rede reinicia o servidor e encerra as sessões atuais. Continuar?","RemoteDesk Server",MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;
        await Operation("Salvando modo de rede…",()=>{
            var previous=ServiceConfiguration.Public(ServiceConfiguration.Root)??throw new IOException("Instale os componentes primeiro.");
            ServiceConfiguration.ConfigureNetwork(ServiceConfiguration.Root,mode,server,relay,port);
            try{if(ServiceStateReader.Read().Code==4){ServiceManager.Stop();WaitState(1);ServiceManager.Start();WaitState(4);}}
            catch{File.WriteAllText(Path.Combine(ServiceConfiguration.Root,"lan.json"),System.Text.Json.JsonSerializer.Serialize(previous));throw;}
        });
    }
    void About_Click(object sender,RoutedEventArgs e)=>MessageBox.Show(this,"RemoteDesk Server\nServidor de sinalização e relay independentes.\nSomente sessões autorizadas pelos clientes.","RemoteDesk Server",MessageBoxButton.OK,MessageBoxImage.Information);
    static PublicConfiguration? Public()=>ServiceConfiguration.Public(ServiceConfiguration.Root)??ServiceConfiguration.Public(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"RemoteDesk","infrastructure"));
    static string DefaultIp()=>NetworkInterface.GetAllNetworkInterfaces().Where(n=>n.OperationalStatus==OperationalStatus.Up&&n.NetworkInterfaceType!=NetworkInterfaceType.Loopback).SelectMany(n=>n.GetIPProperties().UnicastAddresses).FirstOrDefault(a=>a.Address.AddressFamily==AddressFamily.InterNetwork)?.Address.ToString()??"";
    static bool Admin=>new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
    bool EnsureAdmin(string resumeAction)
    {
        if(Admin)return true;
        try
        {
            string executable=Environment.ProcessPath??throw new IOException("Executável não identificado.");
            var start=new ProcessStartInfo(executable){UseShellExecute=true,Verb="runas"};
            if(Path.GetFileNameWithoutExtension(executable).Equals("dotnet",StringComparison.OrdinalIgnoreCase))
            {
                string entry=Path.GetFullPath(Environment.GetCommandLineArgs()[0]);
                if(!Path.GetExtension(entry).Equals(".dll",StringComparison.OrdinalIgnoreCase)||!File.Exists(entry))throw new IOException("Não foi possível identificar o assembly do instalador para elevação.");
                start.ArgumentList.Add(entry);
            }
            start.ArgumentList.Add("--resume-action");start.ArgumentList.Add(resumeAction);
            start.ArgumentList.Add("--ip");start.ArgumentList.Add(IpField.Text.Trim());
            Process.Start(start);Application.Current.Shutdown();
        }
        catch(Exception ex){StatusText.Text="Permissão de administrador necessária: "+ex.Message;}
        return false;
    }
    void Refresh()
    {
        var state=ServiceStateReader.Read();ServiceState.Text=state.Label;
        ServiceState.Foreground=new SolidColorBrush(state.Code==4?Color.FromRgb(126,199,142):Color.FromRgb(200,200,200));
        ApplyServiceState(state.Code);
        try
        {
            var c=Public();ServerAddress.Text=c?.ServerUri??"";RelayAddress.Text=c?.RelayUri??"";ServerFingerprint.Text=c?.CertificateSha256??"";RelayFingerprint.Text=ServerFingerprint.Text;
            ConfigurationHint.Text=c is null?"Os endereços LAN serão exibidos após a instalação.":"Rede local: copie os endereços e a SHA nos clientes. O servidor e o relay usam o mesmo certificado LAN.";
        }
        catch(Exception ex){ConfigurationHint.Text="Não foi possível ler a configuração pública: "+ex.Message;}
    }
    static void WriteInstallLog(string message)
    {
        try{Directory.CreateDirectory(ServiceConfiguration.Root);File.AppendAllText(Path.Combine(ServiceConfiguration.Root,"installer.log"),DateTime.UtcNow.ToString("O")+" "+message+Environment.NewLine);}catch{}
    }
    internal void ResumeAction(string action)
    {
        if(!Admin){StatusText.Text="A permissão de administrador não foi concedida.";return;}
        switch(action)
        {
            case nameof(Install_Click):Install_Click(this,new());break;
            case nameof(Start_Click):Start_Click(this,new());break;
            case nameof(Stop_Click):Stop_Click(this,new());break;
            case nameof(Firewall_Click):Firewall_Click(this,new());break;
            case nameof(Remove_Click):Remove_Click(this,new());break;
        }
    }
    async Task Operation(string status,Action operation,[System.Runtime.CompilerServices.CallerMemberName] string resumeAction="")
    {
        if(busy||!EnsureAdmin(resumeAction))return;
        busy=true;NetworkCard.IsEnabled=false;Actions.IsEnabled=false;IpField.IsEnabled=false;StatusText.Text=status;
        try{await Task.Run(()=>{WriteInstallLog("BEGIN "+resumeAction);operation();WriteInstallLog("SUCCESS "+resumeAction);});Refresh();StatusText.Text=ServiceStateReader.Read().Code==4?"Instalado — serviço em execução. Inicia com o Windows.":"Operação concluída. Estado do serviço atualizado.";}
        catch(Exception ex){StatusText.Text="Falha: "+ex.Message;WriteInstallLog(ex.ToString());MessageBox.Show(this,ex.Message+"\n\nLog: "+Path.Combine(ServiceConfiguration.Root,"installer.log"),"RemoteDesk Server — falha na operação",MessageBoxButton.OK,MessageBoxImage.Error);}
        finally{busy=false;NetworkCard.IsEnabled=true;Actions.IsEnabled=true;Refresh();}
    }
    async void Install_Click(object sender,RoutedEventArgs e)
    {
        string ip=IpField.Text.Trim();
        if(!IPAddress.TryParse(ip,out var parsed)||parsed.AddressFamily!=AddressFamily.InterNetwork||IPAddress.IsLoopback(parsed)){StatusText.Text="Informe um IPv4 válido da rede local.";IpField.Focus();return;}
        if(ServiceManager.Exists()&&MessageBox.Show(this,"Atualizar os componentes aplica o modo e os campos preenchidos e interrompe as conexões atuais. Certificado e IDs serão preservados. Continuar?","RemoteDesk Server",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)return;
        string mode=NetworkMode.SelectedIndex==1?"vps":"local",server=PublicServerField.Text.Trim(),relay=PublicRelayField.Text.Trim();
        if(!int.TryParse(TunnelPortField.Text,out int port)){StatusText.Text="Informe uma porta válida.";return;}
        await Operation("Instalando componentes e preparando o serviço…",()=>InstallerPayload.Install(ip,()=>ServiceConfiguration.ConfigureNetwork(ServiceConfiguration.Root,mode,server,relay,port)));
    }
    async void Start_Click(object sender,RoutedEventArgs e)=>await Operation("Iniciando servidor…",()=>{ServiceManager.Start();WaitState(4);});
    async void Firewall_Click(object sender,RoutedEventArgs e)=>await Operation("Configurando firewall para a rede local…",ServerFirewall.Configure);
    async void Stop_Click(object sender,RoutedEventArgs e)=>await Operation("Parando servidor…",()=>{ServiceManager.Stop();WaitState(1);});
    async void Remove_Click(object sender,RoutedEventArgs e)
    {
        if(MessageBox.Show(this,"Remover o registro do serviço RemoteDesk Server? Configuração e arquivos serão preservados.","RemoteDesk Server",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)return;
        await Operation("Removendo o registro do serviço…",()=>{ServiceManager.Stop();ServiceManager.Remove();});
    }
    void Refresh_Click(object sender,RoutedEventArgs e){Refresh();if(!busy)StatusText.Text="Estado e configuração pública atualizados.";}
    void Copy_Click(object sender,RoutedEventArgs e)
    {
        string value=((Button)sender).Tag?.ToString() switch{"server"=>ServerAddress.Text,"relay"=>RelayAddress.Text,"serverHash"=>ServerFingerprint.Text,"relayHash"=>RelayFingerprint.Text,_=>""};
        try{if(string.IsNullOrEmpty(value)){StatusText.Text="Este dado estará disponível após a instalação.";return;}Clipboard.SetText(value);StatusText.Text="Dado copiado para a área de transferência.";}catch(Exception ex){StatusText.Text="Não foi possível copiar: "+ex.Message;}
    }
}
internal static class InstallerPayload
{
    internal static ZipArchive Open()=>new(Assembly.GetExecutingAssembly().GetManifestResourceStream("RemoteDesk.ServerPayload.zip")??throw new IOException("Pacote do servidor ausente."),ZipArchiveMode.Read);
    internal static IReadOnlyList<(ZipArchiveEntry Entry,string RelativePath)> Validate(ZipArchive zip)
    {
        var files=PayloadPolicy.Validate(zip,"service/RemoteDesk.Service.exe");foreach(var required in new[]{"server/RemoteDesk.Server.exe","relay/RemoteDesk.Relay.exe","uninstaller/RemoteDesk.Uninstall.exe"})if(!files.Any(f=>f.RelativePath.Replace('\\','/')==required&&f.Entry.Length>0))throw new IOException("Componente ausente: "+required);return files;
    }
    static void WaitState(int expected)
    {
        for(int i=0;i<60;i++){int code=ServiceStateReader.Read().Code;if(code==expected)return;if(expected==4&&code==1&&i>2)throw new IOException("O serviço falhou ao iniciar. Confira os logs.");System.Threading.Thread.Sleep(500);}
        throw new IOException("O serviço não atingiu o estado esperado no prazo.");
    }
    internal static void Install(string ip,Action? configureNetwork=null)
    {
        bool existed=ServiceManager.Exists();
        using var zip=Open();var files=Validate(zip);string target=ServiceConfiguration.AppRoot;ServiceConfiguration.CheckPath(target);
        bool managed=File.Exists(Path.Combine(target,".remotedesk-server-owned"));
        if(Directory.Exists(target)&&Directory.EnumerateFileSystemEntries(target).Any()&&!managed)throw new IOException("A pasta de binários existe sem marcador RemoteDesk. Preserve-a antes de instalar.");
        if(!existed)foreach(int port in new[]{5081,5091,5092}){try{var listener=new TcpListener(IPAddress.Loopback,port);listener.Start();listener.Stop();}catch(SocketException){throw new IOException($"Porta {port} ocupada. Encerre a infraestrutura manual antes de instalar.");}}
        string suffix=Guid.NewGuid().ToString("N"),stage=target+".stage-"+suffix,backup=target+".previous-"+suffix;
        ServiceConfiguration.CheckPath(stage);Directory.CreateDirectory(stage);
        foreach(var (entry,relative) in files)
        {
            string destination=Path.GetFullPath(Path.Combine(stage,relative));if(!destination.StartsWith(stage+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new IOException("Caminho inválido.");
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);if(entry.FullName.EndsWith('/'))continue;entry.ExtractToFile(destination,false);
        }
        File.WriteAllText(Path.Combine(stage,".remotedesk-server-owned"),"RemoteDesk Server");
        ServiceConfiguration.Setup(ServiceConfiguration.Root,ip);
        string configFile=Path.Combine(ServiceConfiguration.Root,"lan.json"),previousConfiguration=File.ReadAllText(configFile);
        bool stopped=false,moved=false,installed=false;string step="parar serviço";
        try
        {
            if(existed){ServiceManager.Stop();WaitState(1);stopped=true;}
            step="aplicar configuração de rede";configureNetwork?.Invoke();
            step="substituir binários";
            if(Directory.Exists(target))Directory.Move(target,backup);
            Directory.Move(stage,target);moved=true;step="configurar firewall LAN";ServerFirewall.Configure();
            step="registrar serviço";
            if(!existed){ServiceManager.Install(Path.Combine(target,"service","RemoteDesk.Service.exe"));installed=true;}
            step="iniciar serviço atualizado";ServiceManager.Start();WaitState(4);
            step="registrar em Aplicativos instalados";RemoteDesk.Desktop.InstalledApplication.Register(true);
            // Keep the previous managed binaries as a recovery copy. No recursive deletion of user paths.
        }
        catch(Exception failure)
        {
            try
            {
                if(existed||installed){ServiceManager.Stop();WaitState(1);}
                if(installed)ServiceManager.Remove();
                File.WriteAllText(configFile,previousConfiguration);
                if(moved&&Directory.Exists(target))Directory.Move(target,target+".failed-"+suffix);
                if(Directory.Exists(backup))Directory.Move(backup,target);
                if(existed&&stopped){ServiceManager.Start();WaitState(4);}
            }
            catch(Exception recovery){throw new IOException("Atualização falhou na etapa "+step+" e a restauração requer revisão. Binários anteriores: "+backup,new AggregateException(failure,recovery));}
            throw new IOException("Falha ao "+step+": "+failure.Message+" A versão anterior foi restaurada.",failure);
        }
    }
}
