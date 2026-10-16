using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.IO;
using RemoteDesk.Shared;
namespace RemoteDesk.Client.UI;
public sealed partial class SessionWindow : Window
{
    private readonly System.Windows.Controls.Image screen = new() { Stretch = Stretch.Uniform, Focusable = true };
    private readonly TextBlock state = new() { Text = "Aguardando imagem…", Margin = new Thickness(16,8,16,8) };
    private readonly Permissions permissions;
    private readonly Action<InputEvent> sendInput;
    private readonly System.Windows.Threading.DispatcherTimer moveTimer;
    private InputEvent? pendingMove;
    private readonly HashSet<int> keys = [];
    private readonly HashSet<int> buttons = [];
    private Cursor? remoteCursor;
    internal Button? ViewerFileButton {get;private set;}
    internal bool HasFloatingControls=>island!=null;
    internal bool HasBluePointer=>remoteCursor!=null;
    private SessionIsland? island;
    internal SessionIsland Controls=>island!;
    internal void SetTransmissionLevel(int level)=>island?.QualityControl?.SetLevel(level);
    public bool IsHost { get; }
    public bool Ending { get; set; }
    public event Action? EndRequested;
    public event Action<int>? QualityChanged;
    public event Action? SendFileRequested;
    public event Action? CancelTransferRequested;
    public TextBlock TransferStatus { get; } = new() { Text = "Nenhuma transferência", Margin = new Thickness(10,8,10,8) };
    public ProgressBar TransferProgress { get; } = new() { Minimum=0, Maximum=100, Height=8, Margin=new Thickness(10,0,10,4) };
    public SessionWindow(string peer, bool host, Permissions granted, Action<InputEvent> input,int qualityLevel=50)
    {
        Style = (Style)System.Windows.Application.Current.FindResource(typeof(Window));
        IsHost = host; permissions = granted; sendInput = input; Title = "RemoteDesk — " + peer; Width = host ? 760 : 1120; Height = host ? 320 : 760; MinHeight = host ? 280 : 400; MinWidth = 500;
        if (host) { Topmost = true; ResizeMode = ResizeMode.NoResize; }
        else RemoteDesk.Desktop.WindowSizing.Attach(this,1100,720);
        var dock = new DockPanel(); Content = dock;
        if(!host){DockPanel.SetDock(state,Dock.Bottom);dock.Children.Add(state);}
        if(host)
        {
            SessionIsland.ConfigureWindow(this);
            island=new SessionIsland(this,peer,granted,TransferStatus,TransferProgress,state,()=>EndRequested?.Invoke(),()=>CancelTransferRequested?.Invoke(),level=>QualityChanged?.Invoke(level),qualityLevel);
            Content=island;
        }
        else BuildViewer(peer,qualityLevel);
        if(!host&&!granted.View)state.Text="Sessão de transferência de arquivos autorizada.";
        if (host) state.Text = $"{peer}\nVisualização: {granted.View}  •  Controle: {granted.Control}  •  Arquivos: {granted.Files}\nFechar esta janela encerra a sessão.";
        else { screen.MouseMove += OnMove; screen.MouseDown += OnButton; screen.MouseUp += OnButton; screen.MouseWheel += OnWheel; screen.KeyDown += OnKey; screen.KeyUp += OnKey; screen.LostKeyboardFocus += (_,_) => ReleaseKeys(); Deactivated += (_,_) => ReleaseKeys(); }
        moveTimer = new() { Interval = TimeSpan.FromMilliseconds(32) }; moveTimer.Tick += (_,_) => { if (pendingMove != null) { sendInput(pendingMove); pendingMove = null; } }; moveTimer.Start();
        Closed += (_,_) => { moveTimer.Stop();viewerTimer?.Stop();island?.Dispose();bool notify=!Ending;Ending=true;remoteCursor?.Dispose();ReleaseKeys();if(notify)EndRequested?.Invoke(); };
    }
    private bool Point(out double x,out double y)
    {
        x=y=0; if (screen.Source is not BitmapSource source || !permissions.Control) return false;
        double scale = Math.Min(screen.ActualWidth/source.PixelWidth, screen.ActualHeight/source.PixelHeight);
        double w=source.PixelWidth*scale,h=source.PixelHeight*scale;
        var p=Mouse.GetPosition(screen); x=(p.X-(screen.ActualWidth-w)/2)/w; y=(p.Y-(screen.ActualHeight-h)/2)/h;
        return double.IsFinite(x) && double.IsFinite(y) && x>=0 && x<=1 && y>=0 && y<=1;
    }
    private void OnMove(object sender,MouseEventArgs e) { if (Point(out var x,out var y)) pendingMove = new("move",x,y); }
    private void OnButton(object sender,MouseButtonEventArgs e)
    {
        int b=e.ChangedButton switch { MouseButton.Left=>0,MouseButton.Right=>1,MouseButton.Middle=>2,_=>-1 }; if(b<0||!permissions.Control)return;
        bool down=e.ButtonState==MouseButtonState.Pressed;
        if (!Point(out var x,out var y) && down)return;
        if(down) { screen.Focus(); screen.CaptureMouse(); buttons.Add(b); } else {buttons.Remove(b); if(buttons.Count==0)screen.ReleaseMouseCapture();}
        sendInput(new("button",Math.Clamp(x,0,1),Math.Clamp(y,0,1),b,Down:down));e.Handled=true;
    }
    private void OnWheel(object sender,MouseWheelEventArgs e) { if(Point(out var x,out var y)){sendInput(new("wheel",x,y,Delta:e.Delta));e.Handled=true;} }
    private void OnKey(object sender,System.Windows.Input.KeyEventArgs e) { if(!permissions.Control)return; int vk=KeyInterop.VirtualKeyFromKey(e.Key==Key.System?e.SystemKey:e.Key); if(e.IsDown) keys.Add(vk);else keys.Remove(vk); sendInput(new("key",Key:vk,Down:e.IsDown));e.Handled=true; }
    private void ReleaseKeys() { foreach(var vk in keys)sendInput(new("key",Key:vk,Down:false)); keys.Clear(); foreach(var b in buttons)sendInput(new("button",Button:b,Down:false));buttons.Clear(); }
    public void Frame(string base64)
    {
        var bytes=Convert.FromBase64String(base64); if(bytes.Length>1400000)throw new IOException("Frame excedeu o limite.");
        RemoteDesk.Client.Streaming.JpegHeader.Validate(bytes);
        using var ms=new MemoryStream(bytes); var decoder=BitmapDecoder.Create(ms,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad);
        var bitmap=decoder.Frames[0]; if(bitmap.PixelWidth>4096||bitmap.PixelHeight>4096)throw new IOException("Dimensões inválidas."); bitmap.Freeze();screen.Source=bitmap;UpdateFrameStats(bitmap.PixelWidth,bitmap.PixelHeight,bytes.Length);
    }
}
