using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;
namespace RemoteDesk.Client.UI;
public sealed partial class SessionWindow
{
    DispatcherTimer? viewerTimer;
    readonly Stopwatch viewerElapsed=Stopwatch.StartNew();
    readonly TextBlock topResolution=new(),bottomResolution=new(),topRate=new(),bottomRate=new(),topLatency=new(),bottomLatency=new(),elapsedLabel=new(),fpsLabel=new(),qualityLabel=new();
    ScrollViewer? imageViewport;
    Border? viewerSettings;
    bool fitImage=true,fullScreen;
    WindowState beforeFullScreen;
    int imageWidth,imageHeight,frames;
    long receivedImageBytes,lastSample;
    string? pendingProbe;
    long probeSent;
    public event Action<string>? ProbeRequested;
    internal bool ViewerSettingsVisible=>viewerSettings?.Visibility==Visibility.Visible;
    internal bool FitImage=>fitImage;
    internal Button? ViewerQualityButton {get;private set;}
    internal Button? ViewerFitButton {get;private set;}
    internal Button? ViewerEndButton {get;private set;}
    static Brush Shade(byte r,byte g,byte b)=>new SolidColorBrush(Color.FromRgb(r,g,b));
    static FrameworkElement ViewerIcon(string data,double size=18)
    {
        var canvas=new Canvas{Width=24,Height=24};canvas.Children.Add(new System.Windows.Shapes.Path{Data=Geometry.Parse(data),Stroke=Brushes.White,StrokeThickness=1.7,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round,StrokeLineJoin=PenLineJoin.Round});return new Viewbox{Width=size,Height=size,Child=canvas,VerticalAlignment=VerticalAlignment.Center};
    }
    static Button ViewerAction(string icon,string label,Action action,string? text=null,bool danger=false)
    {
        var content=new StackPanel{Orientation=Orientation.Horizontal};content.Children.Add(ViewerIcon(icon));if(text!=null)content.Children.Add(new TextBlock{Text=text,Margin=new Thickness(8,0,0,0),VerticalAlignment=VerticalAlignment.Center});
        var button=new Button{Content=content,ToolTip=label,Padding=new Thickness(text==null?9:14,10,text==null?9:14,10),Background=danger?Shade(220,35,45):Shade(33,37,42),BorderThickness=new Thickness(0),Margin=new Thickness(3)};
        System.Windows.Automation.AutomationProperties.SetName(button,label);button.Click+=(_,_)=>action();WindowChrome.SetIsHitTestVisibleInChrome(button,true);return button;
    }
    static FrameworkElement Metric(string icon,TextBlock value)
    {
        value.FontSize=12;value.VerticalAlignment=VerticalAlignment.Center;value.Margin=new Thickness(7,0,0,0);var panel=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(10,4,10,4)};panel.Children.Add(ViewerIcon(icon,17));panel.Children.Add(value);return panel;
    }
    void BuildViewer(string peer,int qualityLevel)
    {
        WindowStyle=WindowStyle.None;MinWidth=640;Background=Shade(10,13,16);Icon=System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/UI/RemoteDesk.ico")) is {} resource?LoadIcon(resource.Stream):null;
        WindowChrome.SetWindowChrome(this,new WindowChrome{CaptionHeight=58,ResizeBorderThickness=new Thickness(6),GlassFrameThickness=new Thickness(0),UseAeroCaptionButtons=false});
        var root=new Grid();root.RowDefinitions.Add(new(){Height=new GridLength(58)});root.RowDefinitions.Add(new());root.RowDefinitions.Add(new(){Height=GridLength.Auto});Content=root;
        var header=new Border{Background=Shade(17,21,25),BorderBrush=Shade(37,42,47),BorderThickness=new Thickness(0,0,0,1),Padding=new Thickness(16,0,8,0)};root.Children.Add(header);var headerDock=new DockPanel();header.Child=headerDock;
        var windowActions=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};DockPanel.SetDock(windowActions,Dock.Right);headerDock.Children.Add(windowActions);
        windowActions.Children.Add(ViewerAction("M5 12H19","Minimizar janela",()=>WindowState=WindowState.Minimized));windowActions.Children.Add(ViewerAction("M5 5H19V19H5Z","Maximizar ou restaurar",()=>WindowState=WindowState==WindowState.Maximized?WindowState.Normal:WindowState.Maximized));windowActions.Children.Add(ViewerAction("M6 6L18 18 M18 6L6 18","Fechar sessão",()=>Close()));
        var branding=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};DockPanel.SetDock(branding,Dock.Left);headerDock.Children.Add(branding);
        var mark=new Canvas{Width=256,Height=256};mark.Children.Add(new System.Windows.Shapes.Path{Fill=Shade(29,111,242),Data=Geometry.Parse("M40 32H154Q166 32 174 40L208 74Q216 82 216 94V120Q216 144 192 144H164L224 224H178L106 128Q96 114 114 108H176V96L152 72H80V224H64Q40 224 40 200Z")});branding.Children.Add(new Viewbox{Width=30,Height=30,Child=mark});branding.Children.Add(new TextBlock{Text="RemoteDesk",FontSize=20,FontWeight=FontWeights.SemiBold,Margin=new Thickness(10,0,0,0),VerticalAlignment=VerticalAlignment.Center});
        var caption=new TextBlock{Text="│  Conexão Remota",FontSize=13,Margin=new Thickness(12,0,0,0),VerticalAlignment=VerticalAlignment.Center};branding.Children.Add(caption);
        var topMetrics=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Center};headerDock.Children.Add(topMetrics);
        var connection=new StackPanel();connection.Children.Add(new TextBlock{Text="Sessão ativa",FontSize=10,Foreground=Brushes.LightGray});connection.Children.Add(new TextBlock{Text=peer,FontSize=12,FontWeight=FontWeights.SemiBold,MaxWidth=110,TextTrimming=TextTrimming.CharacterEllipsis,TextWrapping=TextWrapping.NoWrap});var active=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(12,0,10,0)};active.Children.Add(new TextBlock{Text="●",Foreground=Brushes.LimeGreen,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,7,0)});active.Children.Add(connection);topMetrics.Children.Add(active);
        const string monitor="M3 4H21V16H3Z M8 21H16 M12 16V21";const string network="M3 9Q12 1 21 9 M6 13Q12 7 18 13 M9 17Q12 14 15 17 M12 20H12.1";const string latency="M3 8H21 M17 4L21 8L17 12 M21 16H3 M7 12L3 16L7 20";
        topResolution.Text=bottomResolution.Text="— × —";topRate.Text=bottomRate.Text="— Mbps";topLatency.Text=bottomLatency.Text="— ms";
        var resMetric=Metric(monitor,topResolution);var rateMetric=Metric(network,topRate);var latencyMetric=Metric(latency,topLatency);topMetrics.Children.Add(resMetric);topMetrics.Children.Add(rateMetric);topMetrics.Children.Add(latencyMetric);
        var workspace=new Grid{Margin=new Thickness(14)};Grid.SetRow(workspace,1);root.Children.Add(workspace);workspace.ColumnDefinitions.Add(new());workspace.ColumnDefinitions.Add(new(){Width=GridLength.Auto});workspace.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        imageViewport=new ScrollViewer{Content=screen,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,VerticalScrollBarVisibility=ScrollBarVisibility.Disabled,Background=Shade(3,5,7)};workspace.Children.Add(new Border{Background=Shade(3,5,7),BorderBrush=Shade(25,29,33),BorderThickness=new Thickness(1),Child=imageViewport});
        var settingsContent=new StackPanel{Margin=new Thickness(14)};viewerSettings=new Border{Width=280,Visibility=Visibility.Collapsed,Background=Shade(22,26,31),CornerRadius=new CornerRadius(10),Margin=new Thickness(12,0,0,0),Child=new ScrollViewer{Content=settingsContent,VerticalScrollBarVisibility=ScrollBarVisibility.Auto}};Grid.SetColumn(viewerSettings,1);workspace.Children.Add(viewerSettings);
        settingsContent.Children.Add(new TextBlock{Text="Exibição da sessão",FontSize=17,Margin=new Thickness(0,0,0,18)});var balance=new TransmissionControl(qualityLevel);balance.Changed+=level=>{qualityLabel.Text="Solicitado: JPEG "+RemoteDesk.Shared.TransmissionPreset.FromLevel(level).Quality+"%";QualityChanged?.Invoke(level);};settingsContent.Children.Add(balance);qualityLabel.Text="Qualidade: padrão do destino";qualityLabel.FontSize=12;qualityLabel.Margin=new Thickness(0,12,0,12);settingsContent.Children.Add(qualityLabel);settingsContent.Children.Add(new TextBlock{Text=permissions.Control?"Mouse e teclado autorizados":"Somente visualização",FontSize=12,Margin=new Thickness(0,8,0,8)});
        if(permissions.Files){settingsContent.Children.Add(TransferStatus);settingsContent.Children.Add(TransferProgress);settingsContent.Children.Add(ViewerAction("M6 6L18 18 M18 6L6 18","Cancelar transferência",()=>CancelTransferRequested?.Invoke(),"Cancelar transferência"));}
        var rail=new StackPanel{Margin=new Thickness(5,8,5,8)};var railBorder=new Border{VerticalAlignment=VerticalAlignment.Top,Background=Shade(22,26,30),CornerRadius=new CornerRadius(14),Margin=new Thickness(12,0,0,0),Child=rail};Grid.SetColumn(railBorder,2);workspace.Children.Add(railBorder);
        rail.Children.Add(ViewerAction("M4 9V4H9 M15 4H20V9 M20 15V20H15 M9 20H4V15","Tela cheia ou restaurar",ToggleFullScreen));
        ViewerFitButton=ViewerAction(monitor,"Alternar ajuste à janela e tamanho original",ToggleImageFit);rail.Children.Add(ViewerFitButton);
        if(permissions.Control)rail.Children.Add(ViewerAction("M3 6H21V18H3Z M6 10H7 M10 10H11 M14 10H15 M18 10H18.1 M7 14H17","Focar teclado remoto",()=>screen.Focus()));
        ViewerQualityButton=ViewerAction("M5 5H19V19H5Z M8 9H16 M8 12H13 M8 15H16","Qualidade e informações",()=>viewerSettings.Visibility=ViewerSettingsVisible?Visibility.Collapsed:Visibility.Visible);rail.Children.Add(ViewerQualityButton);
        var footer=new Border{Background=Shade(17,21,25),BorderBrush=Shade(37,42,47),BorderThickness=new Thickness(0,1,0,0),Padding=new Thickness(14,8,14,8),MinHeight=66};Grid.SetRow(footer,2);root.Children.Add(footer);var footerDock=new DockPanel();footer.Child=footerDock;
        var actions=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};DockPanel.SetDock(actions,Dock.Right);footerDock.Children.Add(actions);
        ViewerFileButton=ViewerAction("M5 3H14L19 8V21H5Z M14 3V8H19 M8 14H16 M12 10V18","Transferir arquivo",()=>SendFileRequested?.Invoke(),"Transferir arquivo");ViewerFileButton.IsEnabled=permissions.Files;ViewerFileButton.Visibility=permissions.Files?Visibility.Visible:Visibility.Collapsed;actions.Children.Add(ViewerFileButton);
        ViewerEndButton=ViewerAction("M6 6L18 18 M18 6L6 18","Encerrar sessão",()=>EndRequested?.Invoke(),"Encerrar sessão",true);actions.Children.Add(ViewerEndButton);
        var info=new WrapPanel{VerticalAlignment=VerticalAlignment.Center};footerDock.Children.Add(info);var peerState=new TextBlock{Text="●  Conectado\n    "+peer,FontSize=11,Margin=new Thickness(0,2,12,2),Foreground=Brushes.LightGreen};info.Children.Add(peerState);info.Children.Add(Metric(monitor,bottomResolution));info.Children.Add(Metric(network,bottomRate));info.Children.Add(Metric(latency,bottomLatency));info.Children.Add(Metric("M12 3A9 9 0 1 1 11.9 3 M12 7V12L16 14",elapsedLabel));fpsLabel.FontSize=11;fpsLabel.Margin=new Thickness(8);info.Children.Add(fpsLabel);
        if(permissions.Control){using var source=System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/UI/RemotePointer.cur"))!.Stream;remoteCursor=new Cursor(source);screen.Cursor=remoteCursor;}
        SizeChanged+=(_,_)=>{caption.Visibility=ActualWidth>=1000?Visibility.Visible:Visibility.Collapsed;rateMetric.Visibility=ActualWidth>=1100?Visibility.Visible:Visibility.Collapsed;latencyMetric.Visibility=ActualWidth>=1000?Visibility.Visible:Visibility.Collapsed;resMetric.Visibility=ActualWidth>=820?Visibility.Visible:Visibility.Collapsed;if(ActualWidth<780)viewerSettings.Width=220;else viewerSettings.Width=280;};
        PreviewKeyDown+=(_,e)=>{if(fullScreen&&e.Key==Key.Escape){ToggleFullScreen();e.Handled=true;}};
        viewerTimer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(1)};viewerTimer.Tick+=(_,_)=>SampleViewer();viewerTimer.Start();
    }
    static ImageSource LoadIcon(Stream stream){using(stream){var decoder=new System.Windows.Media.Imaging.IconBitmapDecoder(stream,System.Windows.Media.Imaging.BitmapCreateOptions.None,System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);return decoder.Frames[0];}}
    void ToggleImageFit(){fitImage=!fitImage;screen.Stretch=fitImage?Stretch.Uniform:Stretch.None;screen.Width=fitImage||imageWidth==0?double.NaN:imageWidth;screen.Height=fitImage||imageHeight==0?double.NaN:imageHeight;imageViewport!.HorizontalScrollBarVisibility=imageViewport.VerticalScrollBarVisibility=fitImage?ScrollBarVisibility.Disabled:ScrollBarVisibility.Auto;ViewerFitButton!.ToolTip=fitImage?"Imagem ajustada à janela — clique para tamanho original":"Tamanho original — clique para ajustar à janela";}
    void ToggleFullScreen(){if(fullScreen){ResizeMode=ResizeMode.CanResize;WindowState=beforeFullScreen;fullScreen=false;}else{beforeFullScreen=WindowState;ResizeMode=ResizeMode.NoResize;WindowState=WindowState.Maximized;fullScreen=true;}}
    void UpdateFrameStats(int width,int height,int bytes){imageWidth=width;imageHeight=height;receivedImageBytes+=bytes;frames++;topResolution.Text=bottomResolution.Text=$"{width} × {height}";if(!fitImage){screen.Width=width;screen.Height=height;}}
    void SampleViewer()
    {
        long now=viewerElapsed.ElapsedMilliseconds;elapsedLabel.Text=viewerElapsed.Elapsed.ToString(@"hh\:mm\:ss");double seconds=Math.Max(.001,(now-lastSample)/1000d);topRate.Text=bottomRate.Text=$"{receivedImageBytes*8/seconds/1000000:0.00} Mbps";fpsLabel.Text=$"{frames/seconds:0} FPS";receivedImageBytes=0;frames=0;lastSample=now;
        if(permissions.View&&(pendingProbe==null||now-probeSent>5000)){pendingProbe=Guid.NewGuid().ToString("N");probeSent=now;ProbeRequested?.Invoke(pendingProbe);}if(pendingProbe!=null&&now-probeSent>3000)topLatency.Text=bottomLatency.Text="— ms";
    }
    internal void ReceiveProbe(string value){if(value!=pendingProbe)return;topLatency.Text=bottomLatency.Text=$"{Math.Max(0,viewerElapsed.ElapsedMilliseconds-probeSent)} ms";pendingProbe=null;}
}
