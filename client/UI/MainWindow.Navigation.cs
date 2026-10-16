using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using RemoteDesk.Client.Services;
using RemoteDesk.Shared;
namespace RemoteDesk.Client.UI;
public partial class MainWindow
{
    private readonly List<DeviceSummary> discovered = [];
    private List<RecentDevice> displayed = [];
    private string devicePage = "recent";
    private string? selectedDevice;
    private bool listView;
    private DateTime discoveredAt;
    private bool discoveryFresh => connection.Ready && DateTime.UtcNow-discoveredAt<TimeSpan.FromSeconds(30);
    private TextBlock DeviceStatus = new();
    private static readonly Brush Accent = new SolidColorBrush(Color.FromRgb(212,212,212));

    private void BuildInterface()
    {
        profile.Favorites ??= [];profile.Recent ??= [];profile.Devices??=[];
        AddMenu("⚒  Configurações",Settings_Click);
        AddMenu("⚿  Acesso por senha…",(_,_)=>{Settings_Click(this,new());PasswordSettings_Click(this,new());});
        AddMenu("▣  Catálogo de endereços",(_,_)=>{Home_Click(this,new());Recent_Click(this,new());});
        AddMenu("⚿  Configurar servidor…",(_,_)=>{Settings_Click(this,new());NetworkSettings_Click(this,new());});
        AddMenu("ⓘ  Ajuda",(_,_)=>ShowInformation("Ajuda","Informe o identificador do dispositivo de destino no campo superior. O destino deverá aceitar a solicitação e escolher as permissões. Ambos os dispositivos precisam estar conectados ao mesmo servidor.\n\nUse Configurações > Conexão para definir seu servidor. O botão Encerrar interrompe a sessão imediatamente."));
        AddMenu("ⓘ  Sobre o RemoteDesk",About_Click);MenuItems.Children.Add(new Border{Height=1,Background=Brushes.Gray,Margin=new Thickness(0,8,0,8)});
        AddMenu("×  Finalizar",(_,_)=>Close());
        DeviceStatus=StatusTop;
        RefreshDeviceViews();
        SelectHomeTab(NewsTab);
        SettingsScroll.SizeChanged+=(_,_)=>SettingsContent.Width=Math.Min(825,Math.Max(100,SettingsScroll.ActualWidth-20));
        var discoveryExpiry=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromSeconds(5)};
        discoveryExpiry.Tick+=(_,_)=>{if(discovered.Count>0&&!discoveryFresh){discovered.Clear();RefreshDeviceViews();}};discoveryExpiry.Start();Closed+=(_,_)=>discoveryExpiry.Stop();
        var presenceRefresh=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromSeconds(15)};presenceRefresh.Tick+=async(_,_)=>{await RefreshPresence();if(devicePage=="found")RefreshDeviceViews();};presenceRefresh.Start();Closed+=(_,_)=>presenceRefresh.Stop();
        SizeChanged+=(_,_)=>{ArrangeConnectionCards();MenuOverlay.Width=Math.Min(440,Math.Max(280,ActualWidth-48));MenuOverlay.MaxHeight=Math.Max(100,ActualHeight-140);};
        PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Escape)MenuOverlay.Visibility=Visibility.Collapsed;};
        PreviewMouseDown+=(_,_)=>{if(MenuOverlay.Visibility==Visibility.Visible&&!MenuOverlay.IsMouseOver&&!Hamburger.IsMouseOver)MenuOverlay.Visibility=Visibility.Collapsed;};
        foreach(var button in SettingsNavigation.Children.OfType<Button>().Where(b=>!b.IsEnabled))button.ToolTip="Não disponível nesta versão";
    }
    private void AddMenu(string text,RoutedEventHandler? action)
    {
        var b=new Button{Content=text,Height=44,FontSize=20,Background=Brushes.Transparent,BorderThickness=new Thickness(0),Margin=new Thickness(0),Padding=new Thickness(4,5,4,5),HorizontalContentAlignment=HorizontalAlignment.Left,IsEnabled=action!=null};
        if(action!=null)b.Click+=action;else b.ToolTip="Não disponível nesta versão";MenuItems.Children.Add(b);
    }
    private void Menu_Click(object sender,RoutedEventArgs e)
    {
        MenuOverlay.MaxHeight=Math.Max(120,ActualHeight-160);
        MenuOverlay.Visibility=MenuOverlay.Visibility==Visibility.Visible?Visibility.Collapsed:Visibility.Visible;
    }
    private void About_Click(object sender,RoutedEventArgs e)=>ShowInformation("Sobre o RemoteDesk","RemoteDesk 0.2\nAplicação independente de acesso remoto.\n\nSessões com aprovação ou senha habilitada pelo proprietário, permissões separadas, servidor e relay próprios. Captura JPEG por GDI. Gravação e áudio indisponíveis.");
    private void ShowInformation(string title,string text){MenuOverlay.Visibility=Visibility.Collapsed;System.Windows.MessageBox.Show(this,text,"RemoteDesk — "+title,MessageBoxButton.OK,MessageBoxImage.Information);}
    private void IdentityInfo_Click(object sender,RoutedEventArgs e)=>ShowInformation("Este dispositivo","Nome: "+profile.Name+"\nIdentificador: "+profile.DeviceId+"\n\nO identificador permite solicitar uma conexão. Toda sessão exige sua autorização.");
    private void Home_Click(object sender,RoutedEventArgs e){HomePage.Visibility=Visibility.Visible;if(settingsWindow==null)SettingsPage.Visibility=Visibility.Collapsed;PageTitle.Text="Conexão";MenuOverlay.Visibility=Visibility.Collapsed;}
    private void Settings_Click(object sender,RoutedEventArgs e){OpenSettingsWindow();UiSettings_Click(sender,e);}
    private void SelectHomeTab(Button tab)
    {
        foreach(var b in new[]{NewsTab,FavoritesTab,RecentTab,FoundTab,SettingsNav}){b.Foreground=b==tab?Accent:Brushes.Gray;b.FontWeight=b==tab?FontWeights.SemiBold:FontWeights.Normal;b.Background=b==tab?new SolidColorBrush(Color.FromRgb(40,41,43)):Brushes.Transparent;b.BorderBrush=new SolidColorBrush(Color.FromRgb(29,111,242));b.BorderThickness=new Thickness(b==tab?4:0,0,0,0);}
        HomeScroll.ScrollToTop();
    }
    private void News_Click(object sender,RoutedEventArgs e){Home_Click(sender,e);NewsPanel.Visibility=Visibility.Visible;HomeRecentSection.Visibility=Visibility.Visible;HomeFavoriteSection.Visibility=Visibility.Visible;RecentPanel.Visibility=Visibility.Collapsed;SelectHomeTab(NewsTab);RefreshDeviceViews();}
    private void Recent_Click(object sender,RoutedEventArgs e)=>ShowDevices("recent");
    private void Favorites_Click(object sender,RoutedEventArgs e)=>ShowDevices("favorites");
    private async void Found_Click(object sender,RoutedEventArgs e)
    {
        ShowDevices("found");if(!connection.Ready){DeviceListStatus.Text="Conecte-se ao servidor para encontrar dispositivos online.";return;}
        if(serverCapabilityChecked&&!serverPasswordSupported){DeviceListStatus.Text="O servidor instalado é antigo. Atualize os componentes do servidor para consultar status e conectar por senha.";return;}
        await RefreshPresence();
        DeviceListStatus.Text="Consultando dispositivos online no servidor…";
        try{await connection.Send(new(){Type="discover",RequestId=Guid.NewGuid().ToString("N")});}catch(Exception ex){DeviceListStatus.Text=ex.Message;}
    }
    private void ReceiveDevices(Envelope message)
    {
        if(message.Payload is null||message.Payload.Length>1024*1024)throw new InvalidDataException("Lista de dispositivos inválida.");
        var entries=JsonSerializer.Deserialize<DeviceSummary[]>(message.Payload,Wire.Json)??[];
        if(entries.Length>200||entries.Any(d=>string.IsNullOrWhiteSpace(d.Id)||d.Id.Length>64||d.Name?.Length>80))throw new InvalidDataException("Lista de dispositivos inválida.");
        discovered.Clear();discovered.AddRange(entries.Where(d=>d.Id!=profile.DeviceId).DistinctBy(d=>d.Id));discoveredAt=DateTime.UtcNow;RefreshDeviceViews();
    }
    private void ShowDevices(string page)
    {
        Home_Click(this,new());devicePage=page;NewsPanel.Visibility=Visibility.Collapsed;HomeRecentSection.Visibility=Visibility.Collapsed;HomeFavoriteSection.Visibility=Visibility.Collapsed;RecentPanel.Visibility=Visibility.Visible;
        SelectHomeTab(page=="favorites"?FavoritesTab:page=="found"?FoundTab:RecentTab);RefreshDeviceViews();PageTitle.Text=DevicesHeading.Text;
    }
    private void RefreshDeviceViews()
    {
        HomeRecentCards.Children.Clear();foreach(var device in profile.Recent.Take(8))HomeRecentCards.Children.Add(DeviceCard(device));HomeRecentEmpty.Visibility=profile.Recent.Count==0?Visibility.Visible:Visibility.Collapsed;
        HomeFavoriteCards.Children.Clear();foreach(var device in profile.Favorites.Take(8))HomeFavoriteCards.Children.Add(DeviceCard(device));HomeFavoritesEmpty.Visibility=profile.Favorites.Count==0?Visibility.Visible:Visibility.Collapsed;
        var add=new Button{Content="＋\nAdicionar aos favoritos",Width=210,Height=143,Background=Brushes.Transparent,BorderBrush=new SolidColorBrush(Color.FromRgb(65,67,70)),Margin=new Thickness(0,0,12,18)};add.Click+=AddFavorite_Click;HomeFavoriteCards.Children.Add(add);
        displayed=devicePage=="favorites"?profile.Favorites.ToList():devicePage=="found"?profile.Devices.Select(d=>new RecentDevice(d.Id,d.Name,DateTime.UtcNow)).Concat(discovered.Where(d=>!profile.Devices.Any(saved=>saved.Id==d.Id)).Select(d=>new RecentDevice(d.Id,d.Name,DateTime.UtcNow))).ToList():profile.Recent.ToList();
        DevicesHeading.Text=devicePage=="favorites"?"Favoritos":devicePage=="found"?"Dispositivos":"Sessões Recentes";
        AddDeviceButton.Visibility=devicePage=="found"?Visibility.Visible:Visibility.Collapsed;
        RefreshFound.Visibility=devicePage=="found"?Visibility.Visible:Visibility.Collapsed;
        DeviceListStatus.Text=displayed.Count==0?(devicePage=="found"?"Adicione um dispositivo pelo ID ou atualize para encontrar clientes online.":devicePage=="favorites"?"Marque a estrela de um dispositivo para adicioná-lo aos favoritos.":"Nenhuma sessão recente."):devicePage=="found"?"Dispositivos cadastrados e detectados. Status atualizado pelo servidor a cada 15 segundos.":"Selecione um dispositivo. A disponibilidade será confirmada pelo servidor ao solicitar conexão.";
        DeviceCards.Children.Clear();foreach(var device in displayed)DeviceCards.Children.Add(DeviceCard(device));
        RecentList.ItemsSource=displayed.Select(d=>$"{(profile.Favorites.Any(f=>f.Id==d.Id)?"★":"☆")}  {d.Name}   •   {FormatId(d.Id)}").ToArray();
        DeviceCards.Visibility=listView?Visibility.Collapsed:Visibility.Visible;RecentList.Visibility=listView?Visibility.Visible:Visibility.Collapsed;
    }
    private Border DeviceCard(RecentDevice device)
    {
        bool online=DeviceStatusLabel(device.Id)=="Online"||discoveryFresh&&discovered.Any(d=>d.Id==device.Id);
        var stack=new StackPanel();var preview=new Grid{Height=70,Background=new LinearGradientBrush(Color.FromRgb(15,25,49),Color.FromRgb(37,61,104),35),ToolTip="Ilustração do dispositivo; não é uma captura remota"};
        preview.Children.Add(new TextBlock{Text="▱",FontSize=38,Foreground=new SolidColorBrush(Color.FromRgb(112,112,112)),HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center});
        // Gray is unknown; green is based only on a fresh authenticated server discovery response.
        preview.Children.Add(new TextBlock{Text="●",FontSize=14,Foreground=online?Brushes.LimeGreen:Brushes.Gray,Margin=new Thickness(12,8,0,0),VerticalAlignment=VerticalAlignment.Top,ToolTip=online?"Online na última consulta (há menos de 30 segundos)":DeviceStatusLabel(device.Id)});
        var star=new Button{Content=profile.Favorites.Any(f=>f.Id==device.Id)?"★":"☆",FontSize=20,Background=Brushes.Transparent,BorderThickness=new Thickness(0),HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Top,Padding=new Thickness(5),Margin=new Thickness(4),ToolTip="Adicionar ou remover favorito"};
        star.Click+=(_,e)=>{ToggleFavorite(device);e.Handled=true;};preview.Children.Add(star);stack.Children.Add(preview);
        stack.Children.Add(new TextBlock{Text=device.Name,FontSize=14,Margin=new Thickness(12,10,12,2),TextTrimming=TextTrimming.CharacterEllipsis,TextWrapping=TextWrapping.NoWrap});
        stack.Children.Add(new TextBlock{Text=FormatId(device.Id),Foreground=Brushes.Gray,Margin=new Thickness(12,0,12,12)});
        if(profile.Devices.Any(d=>d.Id==device.Id))stack.Children.Add(new TextBlock{Text=DeviceStatusLabel(device.Id),Foreground=DeviceStatusLabel(device.Id)=="Online"?Brushes.LimeGreen:Brushes.Gray,Margin=new Thickness(12,0,12,10)});
        var border=new Border{CornerRadius=new CornerRadius(7),Width=210,Margin=new Thickness(0,0,12,18),Background=new SolidColorBrush(Color.FromRgb(38,38,38)),BorderBrush=new SolidColorBrush(Color.FromRgb(50,50,50)),BorderThickness=new Thickness(1),Child=stack,Cursor=Cursors.Hand};
        if(selectedDevice==device.Id)border.BorderBrush=Accent;
        var saved=profile.Devices.FirstOrDefault(d=>d.Id==device.Id);
        if(saved?.PasswordAccess==true)stack.Children.Add(new TextBlock{Text=string.IsNullOrEmpty(saved.PasswordDpapi)?"Senha a cada conexão":"Senha salva",FontSize=11,Foreground=Brushes.Gray,Margin=new Thickness(12,0,12,10)});
        if(saved!=null){var menu=new ContextMenu();var edit=new MenuItem{Header="Editar dispositivo / senha salva"};edit.Click+=(_,_)=>EditDevice(saved);menu.Items.Add(edit);var remove=new MenuItem{Header="Remover dispositivo"};remove.Click+=(_,_)=>DeleteDevice(saved);menu.Items.Add(remove);border.ContextMenu=menu;}
        border.MouseLeftButtonDown+=(_,e)=>{selectedDevice=device.Id;Address.Text=device.Id;foreach(var panel in new[]{HomeRecentCards,HomeFavoriteCards,DeviceCards})foreach(var item in panel.Children.OfType<Border>())item.BorderBrush=new SolidColorBrush(Color.FromRgb(50,50,50));border.BorderBrush=Accent;if(e.ClickCount==2)Connect_Click(this,e);};return border;
    }
    private void ToggleFavorite(RecentDevice device){if(profile.Favorites.RemoveAll(d=>d.Id==device.Id)==0)profile.Favorites.Add(device);profile.Save();RefreshDeviceViews();}
    private void CardView_Click(object sender,RoutedEventArgs e){listView=false;RefreshDeviceViews();}
    private void ListView_Click(object sender,RoutedEventArgs e){listView=true;RefreshDeviceViews();}
    private RecentDevice? Selected()=>RecentList.SelectedIndex>=0&&RecentList.SelectedIndex<displayed.Count?displayed[RecentList.SelectedIndex]:displayed.FirstOrDefault(d=>d.Id==selectedDevice);
    private void ConnectSelected_Click(object sender,RoutedEventArgs e){var selected=Selected();if(selected!=null){Address.Text=selected.Id;Connect_Click(sender,e);}else DeviceListStatus.Text="Selecione um dispositivo primeiro.";}
    private void Recent_DoubleClick(object sender,MouseButtonEventArgs e)=>ConnectSelected_Click(sender,e);
    private void Rename_Click(object sender,RoutedEventArgs e)
    {
        var device=Selected();if(device==null){DeviceListStatus.Text="Selecione um dispositivo primeiro.";return;}
        var prompt=new PromptWindow(this,"Apelido","Nome exibido para este dispositivo",value:device.Name);if(prompt.ShowDialog()!=true)return;
        var name=prompt.TextValue.Trim();if(name.Length==0||name.Length>80){DeviceListStatus.Text="Use um apelido de 1 a 80 caracteres.";return;}
        foreach(var list in new[]{profile.Recent,profile.Favorites}){int index=list.FindIndex(d=>d.Id==device.Id);if(index>=0)list[index]=list[index] with{Name=name};}
        int registeredIndex=profile.Devices.FindIndex(d=>d.Id==device.Id);if(registeredIndex>=0)profile.Devices[registeredIndex]=profile.Devices[registeredIndex] with{Name=name};
        profile.Save();RefreshDeviceViews();
    }
    private void Copy_Click(object sender,RoutedEventArgs e){if(!string.IsNullOrEmpty(profile.DeviceId)){System.Windows.Clipboard.SetText(profile.DeviceId);Banner.Text="Identificador copiado. Compartilhe-o para receber uma solicitação de conexão.";}}
    private async void Reconnect_Click(object sender,RoutedEventArgs e)=>await Reconnect();
    private void SearchSettings_Changed(object sender,TextChangedEventArgs e){if(SettingsNavigation==null)return;string query=SettingsSearch.Text.Trim();foreach(var b in SettingsNavigation.Children.OfType<Button>())b.Visibility=query.Length==0||b.Content.ToString()!.Contains(query,StringComparison.CurrentCultureIgnoreCase)?Visibility.Visible:Visibility.Collapsed;}
    private void SelectSetting(string title)
    {
        if(settingsWindow!=null)settingsWindow.Title="RemoteDesk — "+title;SettingsScroll.ScrollToTop();
        foreach(var b in SettingsNavigation.Children.OfType<Button>()){bool selected=(string?)b.Tag==title||(title=="Acesso e Permissões"&&(string?)b.Tag=="Acesso");b.Foreground=selected?Accent:b.IsEnabled?Brushes.LightGray:Brushes.Gray;b.BorderBrush=Accent;b.BorderThickness=new Thickness(selected?6:0,0,0,0);}
    }
    private void Section(string title,double top=0){SettingsContent.Children.Add(new TextBlock{Text=title,FontSize=19,Margin=new Thickness(0,top,0,8)});SettingsContent.Children.Add(new Border{Height=1,Background=Brushes.Gray,Margin=new Thickness(0,0,0,15)});}
    private void DisplaySettings_Click(object sender,RoutedEventArgs e){Heading("Exibição");AddQualityFields();}
    private void PermissionsSettings_Click(object sender,RoutedEventArgs e){Heading("Permissões");SettingsContent.Children.Add(new TextBlock{Text="As permissões são escolhidas ao aceitar uma solicitação ou pela política de acesso por senha habilitada neste computador. O acesso por senha é desativado por padrão.",FontSize=14});SettingsContent.Children.Add(new TextBlock{Text="O indicador de sessão permanece visível e permite encerrar imediatamente.",Margin=new Thickness(0,12,0,0)});}
    private void PrivacySettings_Click(object sender,RoutedEventArgs e){Heading("Privacidade");SettingsContent.Children.Add(new TextBlock{Text="A captura de tela começa apenas em uma sessão autorizada. Não há captura global de teclado. Dispositivos em Encontrados são somente os clientes autenticados online no seu servidor.\n\nNão são enviados dados de telemetria. O operador de seu servidor e relay pode acessar o conteúdo retransmitido; a criptografia TLS protege cada conexão.",FontSize=14});}
}
