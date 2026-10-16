using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.IO;
using RemoteDesk.Shared;
using RemoteDesk.Client.Services;
using RemoteDesk.Client.Capture;
using RemoteDesk.Client.RemoteControl;
using RemoteDesk.Client.FileTransfer;
using RemoteDesk.Client.Streaming;
namespace RemoteDesk.Client.UI;
public partial class MainWindow : Window
{
    private readonly Profile profile = Profile.Load();
    private readonly Connection connection = new();
    private readonly ScreenCapture capture = new();
    private readonly InputController input = new();
    private readonly FileManager files;
    private readonly System.Windows.Threading.DispatcherTimer reconnect = new() {Interval=TimeSpan.FromSeconds(10)};
    private SessionWindow? sessionWindow;
    private FileTransferWindow? fileWindow;
    private bool openFilesOnConnect;
    private string? sessionId;
    private string? pendingId;
    private bool host;
    private Permissions granted = new();
    private CancellationTokenSource? stream;
    private bool connecting;
    private bool exiting;
    private readonly SemaphoreSlim incomingQueue = new(1,1);
    private PromptWindow? consent;
    private RelayConnection? relay;
    private Envelope? latestFrame;
    private bool frameBusy;
    private TransmissionPreset? sessionTransmission;
    private RemotePointerWindow? remotePointer;
    private readonly System.Windows.Threading.DispatcherTimer frameTimer = new() {Interval=TimeSpan.FromMilliseconds(33)};
    public MainWindow()
    {
        InitializeComponent();BuildInterface();InitializeTray();LocalId.Text=string.IsNullOrEmpty(profile.DeviceId)?"—":profile.DeviceId;
        RemoteDesk.Desktop.WindowSizing.Attach(this,1100,720);
        files=new FileManager(SendMedia,s=>Dispatcher.BeginInvoke(()=>{if(sessionWindow!=null){sessionWindow.TransferStatus.Text=s; var last=s.Split(' ').LastOrDefault()?.TrimEnd('%');if(s.EndsWith('%')&&double.TryParse(last,out double value))sessionWindow.TransferProgress.Value=value;}}));
        connection.State+=s=>Dispatcher.BeginInvoke(()=>{StatusTop.Text=s;DeviceStatus.Text=s;StatusDot.Foreground=connection.Ready?Brushes.LimeGreen:Brushes.Gray;});
        connection.Message+=DispatchMessage;
        frameTimer.Tick+=async(_,_)=>{if(frameBusy)return;var msg=Interlocked.Exchange(ref latestFrame,null);if(msg==null)return;frameBusy=true;try{await Handle(msg);}finally{frameBusy=false;}};frameTimer.Start();
        Loaded+=async(_,_)=>
        {
            if(Environment.GetCommandLineArgs().Contains("--tray-test")){await CheckTray();return;}
            if(Environment.GetCommandLineArgs().Contains("--ui-test")){await CheckScrolling();return;}
            if(Environment.GetCommandLineArgs().Any(a=>a.StartsWith("--render"))){await Render();return;}
            await Reconnect();reconnect.Start();
        };
        reconnect.Tick+=async(_,_)=>{if(!exiting&&profile.AutoReconnect&&!connection.Ready&&!connecting)await Reconnect();};
        Closing+=(_,_)=>{exiting=true;reconnect.Stop();frameTimer.Stop();Teardown();_ = connection.DisposeAsync();};
    }
    private async Task CheckScrolling()
    {
        try
        {
            if(SettingsPage.Visibility!=Visibility.Collapsed)throw new InvalidOperationException("Configurações abertas na inicialização.");
            await CheckRequestWindow();
            await CheckPasswordPersistence();
            Menu_Click(this,new());if(MenuOverlay.Visibility!=Visibility.Visible||MenuItems.Children.OfType<Button>().Count()!=7)throw new InvalidOperationException("Menu incompleto.");Menu_Click(this,new());
            var fixture=new RecentDevice("111222333","Dispositivo de teste",DateTime.UtcNow);ToggleFavorite(fixture);if(!Profile.Load().Favorites.Any(d=>d.Id==fixture.Id))throw new InvalidOperationException("Favorito não persistido.");Favorites_Click(this,new());if(DeviceCards.Children.Count!=1)throw new InvalidOperationException("Favorito não exibido.");ToggleFavorite(fixture);if(Profile.Load().Favorites.Count!=0)throw new InvalidOperationException("Favorito não removido.");
            await Handle(new(){Type="devices",Payload=System.Text.Json.JsonSerializer.Serialize(new[]{new DeviceSummary("999888777","Dispositivo online de teste")},Wire.Json)});ShowDevices("found");if(discovered.Count!=1||DeviceCards.Children.Count!=1)throw new InvalidOperationException("Resposta devices não atualizou Encontrados.");await Handle(new(){Type="devices",Payload="inválido"});if(discovered.Count!=1)throw new InvalidOperationException("Resposta inválida alterou lista.");discovered.Clear();RefreshDeviceViews();
            var registered=new RemoteDesk.Client.Services.SavedDevice("444555666","Dispositivo cadastrado",true,RemoteDesk.Client.Security.PasswordAccess.Seal("Test-password-8392!"));profile.Devices.Add(registered);profile.Save();
            if(File.ReadAllText(Path.Combine(Profile.DirectoryPath,"settings.json")).Contains("Test-password-8392!"))throw new InvalidOperationException("Senha salva em texto aberto.");
            ShowDevices("found");if(AddDeviceButton.Visibility!=Visibility.Visible||DeviceCards.Children.Count!=1)throw new InvalidOperationException("Cadastro não aparece na aba de dispositivos.");
            var editor=new DeviceEditor(this,registered);if(!editor.SavePassword||editor.Password.Length!=0)throw new InvalidOperationException("Editor revelou ou perdeu a senha salva.");editor.Close();
            await Handle(new(){Type="presence",Payload=System.Text.Json.JsonSerializer.Serialize(new[]{new DevicePresence(registered.Id,registered.Name,false)},Wire.Json)});if(presence[registered.Id].Online)throw new InvalidOperationException("Status offline não aplicado.");profile.Devices.Clear();profile.Save();
            profile.ExtraSettings=new(){["futureTest"]=System.Text.Json.JsonSerializer.SerializeToElement("preservar")};profile.Save();if(Profile.Load().ExtraSettings?["futureTest"].GetString()!="preservar")throw new InvalidOperationException("Campo futuro não preservado.");
            Settings_Click(this,new());SettingsSearch.Text="Conexão";if(SettingsNavigation.Children.OfType<Button>().Count(b=>b.Visibility==Visibility.Visible)!=1)throw new InvalidOperationException("Busca não filtrou menu.");SettingsSearch.Text="";
            NetworkSettings_Click(this,new());
            var networkMode=SettingsContent.Children.OfType<ComboBox>().Single();var networkFields=SettingsContent.Children.OfType<TextBox>().ToArray();
            networkFields[0].Text="wss://192.168.7.55:5081/ws";networkFields[1].Text=new string('A',64);networkFields[2].Text=new string('B',64);
            networkMode.SelectedIndex=1;if(networkFields[1].Text!=""||networkFields[2].Text!="")throw new InvalidOperationException("LAN pins leaked into public mode.");
            networkFields[0].Text="wss://remote.example/ws";networkMode.SelectedIndex=0;
            if(networkFields[0].Text!="wss://192.168.7.55:5081/ws"||networkFields[1].Text!=new string('A',64))throw new InvalidOperationException("Switching mode lost LAN settings.");
            networkMode.SelectedIndex=1;if(networkFields[0].Text!="wss://remote.example/ws")throw new InvalidOperationException("Switching mode lost public address.");
            settingsWindow!.MinHeight=300;settingsWindow.Height=360;settingsWindow.Width=640;MinHeight=300;Height=Math.Min(480,SystemParameters.WorkArea.Height);MinWidth=600;Width=Math.Min(900,SystemParameters.WorkArea.Width);
            Settings_Click(this,new RoutedEventArgs());SecuritySettings_Click(this,new RoutedEventArgs());settingsWindow!.UpdateLayout();if(Window.GetWindow(SettingsPage)!=settingsWindow||HomePage.Visibility!=Visibility.Visible)throw new InvalidOperationException("Settings must be a separate window.");UpdateLayout();await System.Windows.Threading.Dispatcher.Yield();
            if(SettingsScroll.ScrollableHeight<=0)throw new InvalidOperationException("Conteúdo não disponibilizou rolagem.");
            SettingsScroll.ScrollToBottom();UpdateLayout();await System.Windows.Threading.Dispatcher.Yield();
            if(SettingsScroll.VerticalOffset<=0)throw new InvalidOperationException("Não rolou até o final.");
            var save=SettingsContent.Children.OfType<Button>().Last();var position=save.TransformToAncestor(SettingsScroll).Transform(new System.Windows.Point(0,0));
            if(position.Y<0||position.Y+save.ActualHeight>SettingsScroll.ActualHeight+1)throw new InvalidOperationException("Salvar está fora da área visível.");
            SettingsScroll.ScrollToTop();UpdateLayout();await System.Windows.Threading.Dispatcher.Yield();
            SettingsScroll_MouseWheel(SettingsScroll,new MouseWheelEventArgs(Mouse.PrimaryDevice,Environment.TickCount,-120){RoutedEvent=Mouse.PreviewMouseWheelEvent});UpdateLayout();await System.Windows.Threading.Dispatcher.Yield();
            if(SettingsScroll.VerticalOffset<=0)throw new InvalidOperationException("Roda do mouse não rolou.");
            var sidebar=SettingsPage.Children.OfType<ScrollViewer>().Single();if(sidebar.ScrollableHeight<=0)throw new InvalidOperationException("Menu lateral não disponibilizou rolagem.");sidebar.ScrollToBottom();UpdateLayout();await System.Windows.Threading.Dispatcher.Yield();if(sidebar.VerticalOffset<=0)throw new InvalidOperationException("Menu não rolou.");
            DisplaySettings_Click(this,new());settingsWindow!.UpdateLayout();
            var balance=SettingsContent.Children.OfType<TransmissionControl>().Single();balance.Balance.Value=55;
            if(!balance.Balance.IsDirectionReversed)throw new InvalidOperationException("Qualidade deve ficar à esquerda e velocidade à direita.");
            SettingsContent.Children.OfType<Button>().Last().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));if(Profile.Load().Quality!=TransmissionPreset.FromLevel(55).Quality)throw new InvalidOperationException("Qualidade não persistiu.");
            UiSettings_Click(this,new());if(SettingsContent.Children.OfType<CheckBox>().Any(c=>!c.IsEnabled)||SettingsNavigation.Children.OfType<Button>().Any(b=>!b.IsEnabled)||MenuItems.Children.OfType<Button>().Any(b=>!b.IsEnabled))throw new InvalidOperationException("Opções indisponíveis permaneceram na interface.");
            settingsWindow!.Hide();News_Click(this,new());UpdateLayout();await System.Windows.Threading.Dispatcher.Yield();
            if(HomeScroll.ScrollableHeight<=0)throw new InvalidOperationException("Página inicial não rola.");
            var card=(UIElement)LocalCard;card.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice,Environment.TickCount,-120){RoutedEvent=Mouse.PreviewMouseWheelEvent});
            UpdateLayout();await System.Windows.Threading.Dispatcher.Yield();if(HomeScroll.VerticalOffset<=0)throw new InvalidOperationException("Roda sobre cartão não alcança rolagem da página.");
            Menu_Click(this,new());UpdateLayout();await System.Windows.Threading.Dispatcher.Yield();
            if(MenuOverlay.Child is not ScrollViewer menuScroll)throw new InvalidOperationException("Painel do menu ausente.");
            menuScroll.ScrollToBottom();UpdateLayout();await System.Windows.Threading.Dispatcher.Yield();
            var lastAction=MenuItems.Children.OfType<Button>().Last();var lastPosition=lastAction.TransformToAncestor(menuScroll).Transform(new Point(0,0));
            if(lastPosition.Y<0||lastPosition.Y+lastAction.ActualHeight>menuScroll.ActualHeight+1)throw new InvalidOperationException("Fim do menu inacessível.");
            File.WriteAllText(Path.Combine(Profile.DirectoryPath,"ui-tests.txt"),"PASS menu funcional; configurações em janela separada; controle de qualidade;; navegação lateral; favoritos persistem e removem; campos JSON futuros preservados; busca filtra; resposta devices atualiza Encontrados; JSON inválido tratado; janela 900x480; painel e menu rolam; roda roteada sobre cartões; fim do menu compacto acessível; Salvar acessível.");System.Windows.Application.Current.Shutdown(0);
        }
        catch(Exception e){File.WriteAllText(Path.Combine(Profile.DirectoryPath,"ui-tests.txt"),"FAIL "+e);System.Windows.Application.Current.Shutdown(1);}
    }
    private void DispatchMessage(Envelope msg){if(msg.Type=="frame")Interlocked.Exchange(ref latestFrame,msg);else Dispatcher.BeginInvoke(async()=>await Handle(msg));}
    private async Task Render()
    {
        LocalId.Text="123 456 789";StatusTop.Text="Disponível";DeviceStatus.Text="Servidor conectado";StatusDot.Foreground=Brushes.LimeGreen;
        var arguments=Environment.GetCommandLineArgs();bool menu=arguments.Contains("--render-menu");if(menu)MenuOverlay.Visibility=Visibility.Visible;bool settings=arguments.Contains("--render-settings")||arguments.Contains("--render-password-settings")||arguments.Contains("--render-quality-settings");if(settings){Settings_Click(this,new RoutedEventArgs());if(arguments.Contains("--render-password-settings"))PasswordSettings_Click(this,new());else if(arguments.Contains("--render-quality-settings"))DisplaySettings_Click(this,new());else UiSettings_Click(this,new RoutedEventArgs());}bool devices=arguments.Contains("--render-devices");if(devices)ShowDevices("found");
        UpdateLayout();settingsWindow?.UpdateLayout();await Task.Delay(150);FrameworkElement target=settings?(FrameworkElement)settingsWindow!.Content:this;var bitmap=new RenderTargetBitmap((int)target.ActualWidth,(int)target.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(target);
        string path=Path.Combine(Profile.DirectoryPath,devices?"devices-preview.png":settings?"settings-preview.png":menu?"menu-preview.png":"ui-preview.png");using var output=File.Create(path);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));encoder.Save(output);System.Windows.Application.Current.Shutdown();
    }
    private async Task Reconnect()
    {
        if(connecting||exiting)return;connecting=true;
        discovered.Clear();RefreshDeviceViews();
        try{Teardown();await connection.Connect(profile);}catch(Exception e){StatusTop.Text=e.Message;DeviceStatus.Text=e.Message;}finally{connecting=false;}
    }
    private async Task Handle(Envelope msg)
    {
        if(msg.Type=="access-policy"){serverPasswordSupported=true;if(msg.RequestId==policyRequest||msg.RequestId==null)policyAcknowledgement?.TrySetResult(msg.PasswordAccess==publishedPasswordAccess);return;}
        if(msg.Type=="error"&&msg.Error=="invalid session"&&!serverPasswordSupported&&policyAcknowledgement?.Task.IsCompleted==false){policyAcknowledgement.TrySetResult(false);return;}
        // Consent dialogs must not block expiry/end events; media and files are serialized separately.
        if(msg.Type is "ended" or "disconnected" or "rejected" or "relay-disconnected")
        {
            if(msg.Type=="disconnected"){discovered.Clear();RefreshDeviceViews();}
            if(msg.Type=="relay-disconnected"){await EndSession();return;}
            consent?.Close();consent=null;Teardown();Banner.Text=msg.Error switch{"password access denied"=>"Senha inválida, acesso desativado ou bloqueio temporário. Revise a senha salva em Dispositivos.","reject"=>"Conexão recusada pelo computador remoto.","expired"=>"O computador remoto não respondeu a tempo.","disconnected"=>"O computador remoto se desconectou.",_=>msg.Error??"Sessão encerrada"};connectionRequest?.Finish(Banner.Text);return;
        }
        await incomingQueue.WaitAsync();
        try
        {
            switch(msg.Type)
            {
                case "devices":ReceiveDevices(msg);break;
                case "presence":ReceivePresence(msg);break;
                case "password-challenge":await SendPasswordProof(msg);break;
                case "password-proof":await VerifyPasswordProof(msg);break;
                case "ready":serverPasswordSupported=false;serverCapabilityChecked=false;profile.DeviceId=msg.DeviceId??"";profile.Save();LocalId.Text=FormatId(profile.DeviceId);await PublishAccessPolicy();if(!serverPasswordSupported)Banner.Text="Servidor desatualizado: acesso por senha e status online indisponíveis. Atualize os componentes do servidor.";await RefreshPresence();break;
                case "pending":pendingId=msg.SessionId;Banner.Text=msg.PasswordAccess?"Verificando acesso por senha…":"Aguardando autorização do computador remoto…";ShowConnectionRequest(msg);break;
                case "incoming":
                    if(sessionId!=null||pendingId!=null||consent!=null){await connection.Send(new(){Type="reject",SessionId=msg.SessionId});break;}
                    pendingId=msg.SessionId;host=true;
                    if(msg.PasswordAccess||access.Read()?.Enabled==true){pendingPasswordPermissions=msg.Permissions??new();await BeginPasswordChallenge(msg);break;}
                    RestoreFromTray();
                    consent=new PromptWindow(this,"Solicitação de conexão",$"{msg.Name} ({msg.DeviceId}) deseja iniciar uma sessão.\nConceda somente as permissões necessárias.",msg.Permissions??new());
                    if(profile.NotifySound)System.Media.SystemSounds.Beep.Play();var prompt=consent;bool accepted=prompt.ShowDialog()==true;consent=null;
                    if(pendingId==msg.SessionId)await connection.Send(new(){Type=accepted?"respond":"reject",SessionId=msg.SessionId,Permissions=accepted?prompt.Granted:null});
                    break;
                case "started":
                    if(msg.SessionId!=pendingId||sessionId!=null){await connection.Send(new(){Type="end",SessionId=msg.SessionId});break;}
                    sessionId=msg.SessionId;pendingId=null;granted=msg.Permissions??new();
                    host=msg.IsHost??host;
                    connectionRequest?.Complete();connectionRequest=null;
                    CommitPasswordDevice();
                    if(msg.RelayUrl!=null&&msg.RelayTicket!=null)
                    {
                        relay=new RelayConnection();relay.Message+=DispatchMessage;
                        await relay.Connect(msg.RelayUrl,msg.RelayTicket,sessionId!,profile.RelayCertificateSha256);
                    }
                    sessionWindow=new SessionWindow(msg.DeviceId??"Remoto",host,granted,i=>_ = SafeSend(new(){Type="input",SessionId=sessionId,Data=i}),TransmissionPreset.LevelFromQuality(profile.Quality));
                    sessionWindow.EndRequested+=async()=>await EndSession();sessionWindow.SendFileRequested+=ShowFileTransfer;sessionWindow.CancelTransferRequested+=async()=>await files.Cancel();
                    sessionTransmission=null;sessionWindow.QualityChanged+=level=>{if(host)sessionTransmission=TransmissionPreset.FromLevel(level);else _ = SafeSend(new(){Type="quality",SessionId=sessionId,QualityLevel=level});};
                    sessionWindow.ProbeRequested+=value=>_ = SafeSend(new(){Type="probe",SessionId=sessionId,Payload=value});
                    sessionWindow.Show();files.Start(sessionId!,host);if(!host&&granted.Files&&(openFilesOnConnect||!granted.View))ShowFileTransfer();openFilesOnConnect=false;Banner.Text="Sessão remota ativa — use Encerrar para interromper";
                    profile.Recent.RemoveAll(x=>x.Id==msg.DeviceId);profile.Recent.Insert(0,new(msg.DeviceId??"",msg.Name??msg.DeviceId??"Remoto",DateTime.UtcNow));profile.Recent=profile.Recent.Take(50).ToList();profile.Save();RefreshDeviceViews();
                    if(host&&granted.Control)remotePointer=new RemotePointerWindow(sessionWindow);
                    if(host&&granted.View){capture.MonitorIndex=profile.Monitor;capture.MaxWidth=profile.MaxWidth;capture.Quality=profile.Quality;stream=new();_ = Stream(sessionId!,stream.Token);}
                    break;
                case "quality":if(host&&sessionId==msg.SessionId&&granted.View&&msg.QualityLevel is >=0 and <=100){sessionTransmission=TransmissionPreset.FromLevel(msg.QualityLevel.Value);sessionWindow?.SetTransmissionLevel(msg.QualityLevel.Value);}break;
                case "frame":if(!host&&sessionId==msg.SessionId&&granted.View&&sessionWindow!=null)sessionWindow.Frame(msg.Payload??"");break;
                case "probe":if(host&&sessionId==msg.SessionId&&granted.View&&msg.Payload is {Length:32})await SendMedia(new(){Type="probe-reply",SessionId=sessionId,Payload=msg.Payload});break;
                case "probe-reply":if(!host&&sessionId==msg.SessionId)sessionWindow?.ReceiveProbe(msg.Payload??"");break;
                case "input":if(host&&sessionId==msg.SessionId&&granted.Control&&sessionWindow?.IsVisible==true&&msg.Data!=null){input.Apply(msg.Data,capture.Bounds);remotePointer?.Move(msg.Data,capture.Bounds);}break;
                case "file":if(sessionId==msg.SessionId&&granted.Files&&msg.File!=null&&sessionWindow!=null){try{await files.Receive(msg.File,sessionWindow);}catch(Exception ex){sessionWindow.TransferStatus.Text=ex.Message;Banner.Text=ex.Message;}}break;
                case "error":Banner.Text=msg.Error??"Erro de comunicação";break;
            }
        }
        catch(Exception e){Profile.Log("message-error "+e.GetType().Name);if(sessionId!=null||pendingId!=null)await EndSession();Banner.Text=e.Message;connectionRequest?.Finish(e.Message);}
        finally{incomingQueue.Release();}
    }
    private async Task Stream(string id,CancellationToken token)
    {
        try
        {
            while(!token.IsCancellationRequested)
            {
                var transmission=sessionTransmission??new TransmissionPreset(profile.Quality,profile.MaxWidth,profile.Fps);capture.Quality=transmission.Quality;capture.MaxWidth=transmission.Width;var begin=System.Diagnostics.Stopwatch.StartNew();var jpeg=await Task.Run(capture.CaptureJpeg,token);token.ThrowIfCancellationRequested();
                if(jpeg.Length>1400000)throw new IOException("Imagem excedeu limite; reduza resolução ou qualidade.");
                await SendMedia(new(){Type="frame",SessionId=id,Payload=Convert.ToBase64String(jpeg)});
                await Task.Delay(Math.Max(1,1000/transmission.Fps-(int)begin.ElapsedMilliseconds),token);
            }
        }
        catch(OperationCanceledException){}
        catch(Exception e){await Dispatcher.InvokeAsync(async()=>{Banner.Text=e.Message;await EndSession();});}
    }
    private Task SendMedia(Envelope msg)=>relay!=null?relay.Send(msg):connection.Send(msg);
    private async Task SafeSend(Envelope msg){if(sessionId is null||host)return;try{await SendMedia(msg);}catch{await EndSession();}}
    private async Task EndSession()
    {
        string? id=sessionId??pendingId;Teardown();if(id!=null){try{await connection.Send(new(){Type="end",SessionId=id});}catch{}}
        Banner.Text="Sessão encerrada";
    }
    private void Teardown()
    {
        fileWindow?.Close();fileWindow=null;
        remotePointer?.Close();remotePointer=null;
        connectionRequest?.Finish("Conexão encerrada.");challengeNonce=null;authenticatingDevice=null;authenticatingKey=null;newSavedPassword=null;passwordPrompt?.Close();passwordPrompt=null;
        stream?.Cancel();stream?.Dispose();stream=null;Interlocked.Exchange(ref latestFrame,null);input.ReleaseAll();files.Stop();if(relay!=null){_ = relay.DisposeAsync();relay=null;}sessionId=null;pendingId=null;
        if(sessionWindow!=null){sessionWindow.Ending=true;sessionWindow.Close();sessionWindow=null;}granted=new();Profile.Log("session-stopped");
    }
    private async void Connect_Click(object sender,RoutedEventArgs e)
    {
        await RequestConnection(false);
    }
    private async Task RequestConnection(bool forcePassword)
    {
        if(!connection.Ready){Banner.Text="Configure e conecte o servidor primeiro.";return;}if(sessionId!=null||pendingId!=null)return;
        string id=Address.Text.Replace(" ","").Trim();if(string.IsNullOrEmpty(id))return;
        authenticatingDevice=profile.Devices.FirstOrDefault(d=>d.Id==id);newSavedPassword=null;authenticatingKey=null;
        if(!serverCapabilityChecked&&(forcePassword||authenticatingDevice?.PasswordAccess==true)&&policyAcknowledgement!=null){try{await policyAcknowledgement.Task.WaitAsync(TimeSpan.FromSeconds(4));}catch(TimeoutException){serverCapabilityChecked=true;}}
        if(serverCapabilityChecked&&!serverPasswordSupported&&(forcePassword||authenticatingDevice?.PasswordAccess==true)){ShowConnectionRequest(new(){DeviceId=id});Banner.Text="O servidor instalado não suporta acesso por senha. No instalador do servidor, clique em Atualizar componentes. Depois reinicie o RemoteDesk nos dois PCs.";connectionRequest!.Finish(Banner.Text);return;}
        host=false;try{await connection.Send(new(){Type="request",TargetId=id,Permissions=new(true,true,true),PasswordAccess=forcePassword});Banner.Text="Solicitando autorização…";}catch(Exception ex){Banner.Text=ex.Message;}
    }
    private void Address_KeyDown(object sender,System.Windows.Input.KeyEventArgs e){if(e.Key==Key.Enter)Connect_Click(sender,e);}
    private static string FormatId(string id)=>id.Length==9?$"{id[..3]} {id[3..6]} {id[6..]}":id;
    private void Heading(string text){SettingsContent.Children.Clear();SelectSetting(text);Section(text);}
    private void SettingsScroll_MouseWheel(object sender,MouseWheelEventArgs e)
    {
        if(e.Handled)return;
        DependencyObject? current=e.OriginalSource as DependencyObject;
        bool horizontal=(Keyboard.Modifiers&ModifierKeys.Shift)!=0;
        while(current!=null)
        {
            if(current is ScrollViewer viewer)
            {
                double offset=horizontal?viewer.HorizontalOffset:viewer.VerticalOffset;
                double maximum=horizontal?viewer.ScrollableWidth:viewer.ScrollableHeight;
                if(maximum>0&&((e.Delta<0&&offset<maximum)||(e.Delta>0&&offset>0)))
                {
                    if(horizontal)viewer.ScrollToHorizontalOffset(offset-e.Delta);else viewer.ScrollToVerticalOffset(offset-e.Delta);
                    e.Handled=true;return;
                }
            }
            current=current is Visual||current is System.Windows.Media.Media3D.Visual3D?VisualTreeHelper.GetParent(current):LogicalTreeHelper.GetParent(current);
        }
        if(sender is ScrollViewer fallback&&fallback.ScrollableHeight>0&&!horizontal){fallback.ScrollToVerticalOffset(fallback.VerticalOffset-e.Delta);e.Handled=true;}
    }
    private TextBox Field(string label,string value){SettingsContent.Children.Add(new TextBlock{Text=label,FontSize=13,Margin=new Thickness(0,14,0,8)});var box=new TextBox{Text=value};SettingsContent.Children.Add(box);return box;}
    private void SaveButton(Action action){var b=new Button{Content="Salvar",HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,24,0,0)};b.Click+=(_,_)=>{try{action();profile.Save();Banner.Text="Configurações salvas";}catch(Exception e){Banner.Text=e.Message;}};SettingsContent.Children.Add(b);}
    private void NetworkSettings_Click(object sender,RoutedEventArgs e)
    {
        Heading("Conexão");
        var mode=new ComboBox{ItemsSource=new[]{"Rede local (LAN)","VPS / domínio / Cloudflare"},SelectedIndex=profile.NetworkMode=="vps"?1:0,Margin=new Thickness(0,8,0,8)};SettingsContent.Children.Add(mode);
        var server=Field("Servidor de sinalização",profile.Server);
        var pin=Field("SHA-256 do servidor (vazio para certificado público)",profile.ServerCertificateSha256);
        var relayPin=Field("SHA-256 do relay (vazio para certificado público)",profile.RelayCertificateSha256);
        var hint=new TextBlock{TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,12,0,12)};SettingsContent.Children.Add(hint);
        void Explain()=>hint.Text=mode.SelectedIndex==0?"LAN: informe wss://IP-DO-SERVIDOR:5081/ws e copie as duas SHA do servidor.":"Internet: informe wss://SEU-DOMINIO/ws. Com certificado público, deixe as SHA vazias. O endereço do relay é enviado pelo servidor.";
        Explain();
        mode.SelectionChanged+=(_,_)=>{if(mode.SelectedIndex==1){profile.LocalServer=server.Text;profile.LocalServerPin=pin.Text;profile.LocalRelayPin=relayPin.Text;server.Text=profile.PublicServer;pin.Text="";relayPin.Text="";}else{profile.PublicServer=server.Text;server.Text=profile.LocalServer;pin.Text=profile.LocalServerPin;relayPin.Text=profile.LocalRelayPin;}Explain();};
        var auto=new CheckBox{Content="Reconectar automaticamente ao servidor",IsChecked=profile.AutoReconnect};SettingsContent.Children.Add(auto);
        SaveButton(()=>{var uri=new Uri(server.Text.Trim());if(uri.Scheme!="wss"&&!(uri.Scheme=="ws"&&uri.IsLoopback))throw new InvalidOperationException("Use wss://; ws:// apenas no computador local.");foreach(var value in new[]{pin.Text,relayPin.Text})if(!string.IsNullOrWhiteSpace(value)&&Convert.FromHexString(value.Replace(" ","").Replace(":","")).Length!=32)throw new ArgumentException("SHA-256 inválida.");profile.NetworkMode=mode.SelectedIndex==1?"vps":"local";profile.Server=server.Text.Trim();profile.ServerCertificateSha256=pin.Text.Trim();profile.RelayCertificateSha256=relayPin.Text.Trim();if(profile.NetworkMode=="vps")profile.PublicServer=profile.Server;else{profile.LocalServer=profile.Server;profile.LocalServerPin=pin.Text;profile.LocalRelayPin=relayPin.Text;}profile.AutoReconnect=auto.IsChecked==true;_ = Reconnect();});
    }
    private void UiSettings_Click(object sender,RoutedEventArgs e)
    {
        Heading("Interface do Usuário");
        SettingsContent.Children.Add(new TextBlock{Text="Português (Brasil) · Tema escuro",Margin=new Thickness(0,8,0,16)});
        var sound=new CheckBox{Content="Som na solicitação de conexão recebida",IsChecked=profile.NotifySound,FontSize=13};SettingsContent.Children.Add(sound);
        SaveButton(()=>profile.NotifySound=sound.IsChecked==true);
    }
    private void AddQualityFields()
    {
        var balance=new TransmissionControl(TransmissionPreset.LevelFromQuality(profile.Quality));SettingsContent.Children.Add(balance);
        SettingsContent.Children.Add(new TextBlock{Text="À esquerda, mais detalhes e maior uso da rede. À direita, menor resolução e compressão maior para priorizar fluidez. Durante a sessão, ajuste também nos detalhes da barra flutuante.",FontSize=12,Margin=new Thickness(0,10,0,4)});
        var monitor=Field($"Monitor (0–{capture.MonitorCount-1})",profile.Monitor.ToString());
        SaveButton(()=>{int m=int.Parse(monitor.Text);if(m<0||m>=capture.MonitorCount)throw new ArgumentException("Monitor inválido.");if(sessionId!=null&&m!=profile.Monitor)throw new InvalidOperationException("Encerre a sessão para trocar de monitor.");var preset=TransmissionPreset.FromLevel(balance.Level);profile.Quality=preset.Quality;profile.MaxWidth=preset.Width;profile.Fps=preset.Fps;profile.Monitor=m;if(host)sessionTransmission=null;});
    }
    private void SecuritySettings_Click(object sender,RoutedEventArgs e)
    {
        Heading("Acesso e Permissões");SettingsContent.Children.Add(new TextBlock{Text="Por padrão, as sessões exigem aceitação local. O proprietário pode habilitar Acesso por senha neste computador, com permissões próprias.\n\nFechar o indicador encerra a sessão. A chave do dispositivo é protegida por DPAPI do usuário Windows.",FontSize=13});
        var pin=Field("Certificado do servidor (SHA-256, opcional)",profile.ServerCertificateSha256);var relayPin=Field("Certificado do relay (SHA-256, opcional)",profile.RelayCertificateSha256);
        SettingsContent.Children.Add(new TextBlock{Text="Sem impressão configurada, o Windows deve confiar no certificado. Na rede local, copie a impressão diretamente do computador servidor; confirme por um canal confiável.",Margin=new Thickness(0,16,0,0)});
        SaveButton(()=>{foreach(var value in new[]{pin.Text,relayPin.Text})if(!string.IsNullOrWhiteSpace(value)&&Convert.FromHexString(value.Replace(" ","").Replace(":","")).Length!=32)throw new ArgumentException("Impressão inválida.");profile.ServerCertificateSha256=pin.Text;profile.RelayCertificateSha256=relayPin.Text;_ = Reconnect();});
        SettingsContent.Children.Add(new TextBlock{Text=AdministratorMode.IsElevated?"Modo administrador ativo neste computador.":"Para controlar aplicativos executados como administrador, reinicie o RemoteDesk elevado neste computador. A confirmação UAC deve ser feita localmente. A área segura do Windows não é transmitida.",Margin=new Thickness(0,18,0,8),FontSize=12});
        if(!AdministratorMode.IsElevated){var elevate=new Button{Content="Executar como administrador",HorizontalAlignment=HorizontalAlignment.Left};elevate.Click+=(_,_)=>RestartAsAdministrator();SettingsContent.Children.Add(elevate);}
    }
    private void IdentitySettings_Click(object sender,RoutedEventArgs e){Heading("Identificação");var name=Field("Nome deste dispositivo",profile.Name);SettingsContent.Children.Add(new TextBlock{Text="Identificador: "+profile.DeviceId,Margin=new Thickness(0,20,0,0)});SaveButton(()=>{if(string.IsNullOrWhiteSpace(name.Text)||name.Text.Length>64)throw new ArgumentException("Use um nome de 1 a 64 caracteres.");profile.Name=name.Text;_ = Reconnect();});}
}
