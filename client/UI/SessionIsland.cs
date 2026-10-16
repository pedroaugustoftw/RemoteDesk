using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using RemoteDesk.Shared;
namespace RemoteDesk.Client.UI;
internal sealed class SessionIsland : Border, IDisposable
{
    internal const double IdleOpacity=.42;
    readonly Window window;
    readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromSeconds(1)};
    readonly Stopwatch elapsed=Stopwatch.StartNew();
    readonly StackPanel details;
    readonly ScrollViewer detailsScroll;
    readonly TextBlock peerLabel;
    readonly FrameworkElement identityIcon;
    internal TextBlock Clock {get;}=new(){Text="00:00:00",FontSize=10,Foreground=Brushes.LightGray};
    internal Button EndButton {get;}
    internal Button MinimizeButton {get;}
    internal Button RestoreButton {get;}
    internal Button DetailsButton {get;}
    internal bool IsMinimized {get;private set;}
    internal TransmissionControl? QualityControl {get;}
    internal bool Expanded=>detailsScroll.Visibility==Visibility.Visible;
    public SessionIsland(Window owner,string peer,Permissions granted,TextBlock transfer,ProgressBar progress,TextBlock state,Action end,Action cancel,Action<int> quality,int qualityLevel)
    {
        window=owner;CornerRadius=new CornerRadius(20);Background=new SolidColorBrush(Color.FromRgb(16,17,18));BorderBrush=new SolidColorBrush(Color.FromRgb(61,63,66));BorderThickness=new Thickness(1);Padding=new Thickness(10,7,10,7);
        var root=new StackPanel();Child=root;var row=new Grid();row.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});root.Children.Add(row);
        var identity=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};
        identityIcon=Icon("M3 4H21V16H3Z M8 21H16 M12 16V21");identityIcon.Margin=new Thickness(2,0,8,0);identity.Children.Add(identityIcon);
        var label=new StackPanel{VerticalAlignment=VerticalAlignment.Center};peerLabel=new TextBlock{Text=peer,FontWeight=FontWeights.SemiBold,FontSize=11,MaxWidth=125,TextTrimming=TextTrimming.CharacterEllipsis,TextWrapping=TextWrapping.NoWrap};label.Children.Add(peerLabel);
        var status=new StackPanel{Orientation=Orientation.Horizontal,ToolTip="Sessão remota ativa"};status.Children.Add(new TextBlock{Text="●",Foreground=Brushes.LimeGreen,Margin=new Thickness(0,0,5,0),FontSize=10});status.Children.Add(Clock);label.Children.Add(status);identity.Children.Add(label);row.Children.Add(identity);
        var actions=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};Grid.SetColumn(actions,1);row.Children.Add(actions);
        DetailsButton=ActionButton("M6 9L12 15L18 9","Mostrar ou ocultar detalhes",()=>SetExpanded(!Expanded));actions.Children.Add(DetailsButton);
        MinimizeButton=ActionButton("M5 12H19","Minimizar barra",()=>SetMinimized(true));actions.Children.Add(MinimizeButton);
        RestoreButton=ActionButton("M4 9V4H9 M15 4H20V9 M20 15V20H15 M9 20H4V15","Restaurar barra",()=>SetMinimized(false));RestoreButton.Visibility=Visibility.Collapsed;actions.Children.Add(RestoreButton);
        EndButton=ActionButton("M6 6L18 18 M18 6L6 18","Encerrar sessão",end,true);actions.Children.Add(EndButton);
        details=new StackPanel{Margin=new Thickness(2,10,2,2)};detailsScroll=new ScrollViewer{Content=details,Visibility=Visibility.Collapsed,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,MaxHeight=Math.Max(80,SystemParameters.WorkArea.Height-100)};root.Children.Add(detailsScroll);
        if(granted.View){QualityControl=new TransmissionControl(qualityLevel);QualityControl.Changed+=quality;details.Children.Add(QualityControl);}
        state.FontSize=11;state.Margin=new Thickness(0,6,0,6);transfer.FontSize=11;details.Children.Add(state);if(granted.Files){details.Children.Add(transfer);details.Children.Add(progress);}
        var cancelButton=new Button{ToolTip="Cancelar transferência",HorizontalAlignment=HorizontalAlignment.Right,IsEnabled=granted.Files,FontSize=11,Padding=new Thickness(8,5,8,5)};var cancelContent=new StackPanel{Orientation=Orientation.Horizontal};cancelContent.Children.Add(Icon("M6 6L18 18 M18 6L6 18"));cancelContent.Children.Add(new TextBlock{Text="Cancelar transferência",Margin=new Thickness(6,0,0,0)});cancelButton.Content=cancelContent;cancelButton.Click+=(_,_)=>cancel();if(granted.Files)details.Children.Add(cancelButton);
        MouseEnter+=(_,_)=>SetHover(true);MouseLeave+=(_,_)=>SetHover(false);
        GotKeyboardFocus+=(_,_)=>SetHover(true);LostKeyboardFocus+=(_,_)=>{if(!IsMouseOver)SetHover(false);};
        timer.Tick+=(_,_)=>Clock.Text=elapsed.Elapsed.ToString(@"hh\:mm\:ss");timer.Start();
        owner.Opacity=IdleOpacity;
        identity.MouseLeftButtonDown+=(_,e)=>{if(e.ClickCount==2)SetExpanded(!Expanded);};
    }
    static FrameworkElement Icon(string geometry)
    {
        var canvas=new Canvas{Width=24,Height=24};canvas.Children.Add(new System.Windows.Shapes.Path{Data=Geometry.Parse(geometry),Stroke=Brushes.White,StrokeThickness=2,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round,StrokeLineJoin=PenLineJoin.Round});return new Viewbox{Width=16,Height=16,Child=canvas,VerticalAlignment=VerticalAlignment.Center};
    }
    static Button ActionButton(string geometry,string label,Action action,bool danger=false)
    {
        var b=new Button{Content=Icon(geometry),ToolTip=label,Width=28,Height=28,Margin=new Thickness(2,0,2,0),Padding=new Thickness(0),Background=new SolidColorBrush(danger?Color.FromRgb(241,47,54):Color.FromRgb(39,40,42)),BorderThickness=new Thickness(0)};
        var border=new FrameworkElementFactory(typeof(Border));border.SetValue(Border.CornerRadiusProperty,new CornerRadius(20));border.SetBinding(Border.BackgroundProperty,new System.Windows.Data.Binding("Background"){RelativeSource=new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent)});
        var content=new FrameworkElementFactory(typeof(ContentPresenter));content.SetValue(FrameworkElement.HorizontalAlignmentProperty,HorizontalAlignment.Center);content.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center);border.AppendChild(content);
        var template=new ControlTemplate(typeof(Button)){VisualTree=border};template.Triggers.Add(new Trigger{Property=IsEnabledProperty,Value=false,Setters={new Setter(OpacityProperty,.4)}});b.Template=template;
        System.Windows.Automation.AutomationProperties.SetName(b,label);
        b.Click+=(_,_)=>action();return b;
    }
    internal void SetHover(bool value)=>window.Opacity=value?1:IdleOpacity;
    internal void SetExpanded(bool value)
    {
        if(value&&IsMinimized)SetMinimized(false);
        detailsScroll.Visibility=value?Visibility.Visible:Visibility.Collapsed;DetailsButton.Content=Icon(value?"M6 15L12 9L18 15":"M6 9L12 15L18 9");ResizeBar();
    }
    internal void SetMinimized(bool value)
    {
        IsMinimized=value;if(value){detailsScroll.Visibility=Visibility.Collapsed;DetailsButton.Content=Icon("M6 9L12 15L18 9");}
        peerLabel.Visibility=identityIcon.Visibility=value?Visibility.Collapsed:Visibility.Visible;
        DetailsButton.Visibility=MinimizeButton.Visibility=value?Visibility.Collapsed:Visibility.Visible;RestoreButton.Visibility=value?Visibility.Visible:Visibility.Collapsed;
        EndButton.Width=EndButton.Height=RestoreButton.Width=RestoreButton.Height=value?24:28;Padding=value?new Thickness(8,5,8,5):new Thickness(10,7,10,7);ResizeBar();
    }
    void ResizeBar(){window.Width=Math.Min(IsMinimized?180:Expanded?380:360,Math.Max(160,SystemParameters.WorkArea.Width-24));window.SizeToContent=SizeToContent.Height;window.InvalidateMeasure();Position(window);}
    internal static void ConfigureWindow(Window target)
    {
        target.WindowStyle=WindowStyle.None;target.ResizeMode=ResizeMode.NoResize;target.AllowsTransparency=true;target.Background=Brushes.Transparent;
        target.Topmost=true;target.ShowInTaskbar=false;target.ShowActivated=false;target.MinWidth=0;target.MinHeight=0;
        target.Width=Math.Min(360,SystemParameters.WorkArea.Width-24);target.SizeToContent=SizeToContent.Height;
        target.WindowStartupLocation=WindowStartupLocation.Manual;Position(target);
    }
    internal static void Position(Window target)
    {
        var area=SystemParameters.WorkArea;target.Left=area.Left+(area.Width-target.Width)/2;target.Top=area.Top+10;
    }
    public void Dispose(){timer.Stop();elapsed.Stop();}
}
