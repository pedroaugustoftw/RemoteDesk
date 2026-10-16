using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using RemoteDesk.Client.FileTransfer;
using RemoteDesk.Shared;

namespace RemoteDesk.Client.UI;

public sealed class FileTransferWindow : Window
{
    readonly FileManager manager;
    readonly TaskCompletionSource ready=new(TaskCreationOptions.RunContinuationsAsynchronously);
    readonly ExplorerPane local,remote;
    readonly TextBlock status=new(){Text="Selecione arquivos e escolha a direção da transferência.",TextWrapping=TextWrapping.Wrap};
    readonly TextBlock destination=new(),selected=new();
    readonly ListBox queue=new(){MinHeight=65,MaxHeight=130,Background=Brushes.Transparent,Foreground=Brushes.White,BorderThickness=new Thickness(0)};
    readonly ProgressBar progress=new(){Minimum=0,Maximum=100,Height=8,Margin=new Thickness(0,12,0,8)};
    readonly CheckBox replace=new(){Content="Substituir arquivos existentes",Margin=new Thickness(0,12,0,6)};
    readonly Button start,cancel;
    bool upload=true,transferring,closing;
    CancellationTokenSource? transfer;
    static Brush Shade(byte x)=>new SolidColorBrush(Color.FromRgb(x,x,x));
    static readonly Brush Blue=new SolidColorBrush(Color.FromRgb(29,111,242));
    public FileTransferWindow(Window owner,string peer,FileManager files,Action history,Action settings)
    {
        manager=files;Owner=owner;Title="RemoteDesk — Transferência de arquivos";Width=1280;Height=780;MinWidth=760;MinHeight=500;
        Background=Shade(14);Foreground=Brushes.White;FontFamily=new FontFamily("Segoe UI");FontSize=13;
        RemoteDesk.Desktop.WindowSizing.Attach(this,1280,780);
        var root=new Grid();root.RowDefinitions.Add(new(){Height=GridLength.Auto});root.RowDefinitions.Add(new());Content=root;
        var header=new DockPanel{Margin=new Thickness(22,14,22,14)};root.Children.Add(header);
        var live=new TextBlock{Text="●  Sessão ativa  "+peer,Foreground=Brushes.LightGreen,VerticalAlignment=VerticalAlignment.Center};DockPanel.SetDock(live,Dock.Right);header.Children.Add(live);
        var mark=new System.Windows.Shapes.Path{Data=Geometry.Parse("M40 32H154Q166 32 174 40L208 74Q216 82 216 94V120Q216 144 192 144H164L224 224H178L106 128Q96 114 114 108H176V96L152 72H80V224H64Q40 224 40 200Z"),Fill=Blue,Stretch=Stretch.Uniform,Width=28,Height=28,Margin=new Thickness(0,0,10,0)};header.Children.Add(mark);
        header.Children.Add(new TextBlock{Text="RemoteDesk  │  Transferência de arquivos",FontSize=20,FontWeight=FontWeights.SemiBold});
        var workspace=new Grid();workspace.ColumnDefinitions.Add(new(){Width=new GridLength(190)});workspace.ColumnDefinitions.Add(new());Grid.SetRow(workspace,1);root.Children.Add(workspace);
        var menu=new StackPanel{Margin=new Thickness(8,18,8,8)};workspace.Children.Add(menu);
        menu.Children.Add(ActionButton("▣  Conexão",()=>{owner.Activate();owner.WindowState=WindowState.Normal;}));
        var current=ActionButton("▤  Transferência de arquivos",()=>Activate());current.Background=Blue;menu.Children.Add(current);
        menu.Children.Add(ActionButton("◷  Histórico",history));menu.Children.Add(ActionButton("⚙  Configurações",settings));
        var body=new Grid{Margin=new Thickness(16,0,16,14)};Grid.SetColumn(body,1);workspace.Children.Add(body);body.RowDefinitions.Add(new(){Height=GridLength.Auto});body.RowDefinitions.Add(new());body.RowDefinitions.Add(new(){Height=GridLength.Auto});
        var description=new StackPanel();description.Children.Add(new TextBlock{Text="Transferência de arquivos",FontSize=22,FontWeight=FontWeights.SemiBold});description.Children.Add(new TextBlock{Text="Envie e receba arquivos entre este computador e o computador remoto.",Foreground=Brushes.LightGray,Margin=new Thickness(0,7,0,0)});body.Children.Add(Card(description,new Thickness(0,0,0,12)));
        var panes=new Grid();panes.ColumnDefinitions.Add(new());panes.ColumnDefinitions.Add(new(){Width=new GridLength(48)});panes.ColumnDefinitions.Add(new());Grid.SetRow(panes,1);body.Children.Add(panes);
        local=new ExplorerPane("Meu computador (local)",(path,offset)=>Task.Run(()=>FileBrowser.Read(path,offset)),path=>{Directory.CreateDirectory(FileBrowser.Normalize(path));return Task.CompletedTask;},UpdatePlan,Report);
        remote=new ExplorerPane("Computador remoto",files.Browser.List,async path=>{await files.Browser.CreateFolder(path);},UpdatePlan,Report);
        panes.Children.Add(local);Grid.SetColumn(remote,2);panes.Children.Add(remote);
        var arrows=new StackPanel{VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(4)};Grid.SetColumn(arrows,1);panes.Children.Add(arrows);
        var send=ActionButton("→",()=>{if(!transferring){upload=true;UpdatePlan();}});send.ToolTip="Enviar: computador local → remoto";send.Background=Blue;arrows.Children.Add(send);
        var receive=ActionButton("←",()=>{if(!transferring){upload=false;UpdatePlan();}});receive.ToolTip="Receber: computador remoto → local";arrows.Children.Add(receive);
        var bottom=new Grid{Margin=new Thickness(0,12,0,0)};bottom.ColumnDefinitions.Add(new());bottom.ColumnDefinitions.Add(new(){Width=new GridLength(12)});bottom.ColumnDefinitions.Add(new());Grid.SetRow(bottom,2);body.Children.Add(bottom);
        var selection=new StackPanel();selection.Children.Add(selected);selection.Children.Add(queue);bottom.Children.Add(Card(selection,new Thickness(0)));
        var controls=new StackPanel();controls.Children.Add(destination);controls.Children.Add(replace);controls.Children.Add(progress);controls.Children.Add(status);
        var actions=new WrapPanel{Margin=new Thickness(0,10,0,0)};start=ActionButton("↥  Iniciar transferência",()=>_ = Transfer());start.Background=Blue;actions.Children.Add(start);cancel=ActionButton("Cancelar",()=>_ = Cancel());cancel.IsEnabled=false;actions.Children.Add(cancel);controls.Children.Add(actions);
        var controlCard=Card(controls,new Thickness(0));Grid.SetColumn(controlCard,2);bottom.Children.Add(controlCard);
        manager.ProgressChanged+=OnProgress;
        Loaded+=async(_,_)=>{string downloads=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"Downloads");await local.Go(Directory.Exists(downloads)?downloads:Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));await remote.Go("");UpdatePlan();ready.TrySetResult();};
        Closed+=(_,_)=>{closing=true;manager.ProgressChanged-=OnProgress;transfer?.Cancel();if(transferring)_=manager.Cancel();};
        SizeChanged+=(_,_)=>workspace.ColumnDefinitions[0].Width=new GridLength(ActualWidth<1050?150:190);
    }
    internal async Task CheckInterface(string folder)
    {
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(20));await local.Go(folder);await remote.Go(folder);UpdateLayout();
        if(local.EntryCount<2||remote.EntryCount<2||local.CurrentPath!=remote.CurrentPath)throw new Exception("Local or remote explorer did not load.");
        local.CheckTwoFiles();upload=true;UpdatePlan();if(!start.IsEnabled||queue.Items.Count!=2)throw new Exception("Upload selection did not enable transfer.");
        remote.SelectFirstFile();upload=false;UpdatePlan();if(!start.IsEnabled||!selected.Text.Contains("Receber"))throw new Exception("Download direction did not update.");
        upload=true;UpdatePlan();
        foreach(bool compact in new[]{false,true,false})
        {
            Width=compact?900:1280;Height=compact?600:780;UpdateLayout();await System.Windows.Threading.Dispatcher.Yield();
            if(start.ActualWidth<=0||local.ActualWidth<=0||remote.ActualWidth<=0)throw new Exception("Transfer layout collapsed.");
            var point=start.TransformToAncestor((Visual)Content).Transform(new Point());if(point.Y+start.ActualHeight>((FrameworkElement)Content).ActualHeight+1)throw new Exception("Transfer action outside window.");
        }
    }
    static Border Card(UIElement child,Thickness margin)=>new(){Child=child,Background=Shade(20),BorderBrush=Shade(43),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(7),Padding=new Thickness(14),Margin=margin};
    internal static Button ActionButton(string text,Action action)
    {
        var button=new Button{Content=text,Padding=new Thickness(10,8,10,8),Margin=new Thickness(0,0,5,6),MinHeight=34,Background=Shade(35),Foreground=Brushes.White,BorderBrush=Shade(58),Cursor=Cursors.Hand};button.Click+=(_,_)=>action();return button;
    }
    void Report(string text){if(!closing)status.Text=text;}
    void OnProgress(string text)=>Dispatcher.BeginInvoke(()=>{
        if(closing)return;status.Text=text;var value=text.Split(' ').LastOrDefault()?.TrimEnd('%');if(text.EndsWith('%')&&double.TryParse(value,out var percent))progress.Value=percent;
        if(text.Contains("SHA-256 verificado")||text.Contains("confirmou integridade"))progress.Value=100;
    });
    void UpdatePlan()
    {
        if(local is null||remote is null||transferring)return;
        var source=upload?local:remote;var target=upload?remote:local;
        var entries=source.SelectedFiles;selected.Text=$"{entries.Length} arquivo(s) selecionado(s) • {(upload?"Enviar →":"← Receber")}";
        queue.Items.Clear();foreach(var entry in entries){var row=new DockPanel();var remove=ActionButton("×",()=>{if(!transferring)source.Unselect(entry.Path);});DockPanel.SetDock(remove,Dock.Right);row.Children.Add(remove);row.Children.Add(new TextBlock{Text=$"{entry.Name}    {ExplorerPane.Size(entry.Size)}",VerticalAlignment=VerticalAlignment.Center});queue.Items.Add(row);}destination.Text="Destino: "+(target.CurrentPath.Length==0?"Escolha uma pasta":target.CurrentPath);start.IsEnabled=entries.Length>0&&target.CurrentPath.Length>0&&!local.IsLoading&&!remote.IsLoading;
    }
    async Task Transfer()
    {
        if(transferring)return;var source=upload?local:remote;var target=upload?remote:local;var entries=source.SelectedFiles;
        if(entries.Length==0||target.CurrentPath.Length==0||local.IsLoading||remote.IsLoading)return;string destinationPath=target.CurrentPath;
        if(MessageBox.Show(this,$"{(upload?"Enviar":"Receber")} {entries.Length} arquivo(s) para:\n{destinationPath}\n\nO recebimento e a integridade serão confirmados.","RemoteDesk — confirmar destino",MessageBoxButton.OKCancel)!=MessageBoxResult.OK)return;
        transferring=true;transfer=new();start.IsEnabled=false;cancel.IsEnabled=true;queue.IsEnabled=false;local.IsEnabled=remote.IsEnabled=replace.IsEnabled=false;int completed=0;
        try
        {
            foreach(var entry in entries)
            {
                transfer.Token.ThrowIfCancellationRequested();progress.Value=0;string output=RemotePath.Combine(destinationPath,entry.Name);
                if(!replace.IsChecked.GetValueOrDefault()&&(upload?target.HasFile(entry.Name):File.Exists(output))){Report("Já existe: "+entry.Name+". Marque Substituir para sobrescrever.");continue;}
                if(upload){if(!await manager.SendPath(entry.Path,remoteDestination:output,overwrite:replace.IsChecked==true))throw new IOException("Transferência recusada, cancelada ou incompleta.");}
                else await manager.Download(entry.Path,output,replace.IsChecked==true);
                transfer.Token.ThrowIfCancellationRequested();completed++;
            }
            await target.Reload();Report($"Concluídos {completed} de {entries.Length} arquivo(s), com SHA-256 verificado.");
        }
        catch(OperationCanceledException){Report("Transferência cancelada.");}catch(Exception ex){Report(ex.Message);}
        finally{transferring=false;transfer.Dispose();transfer=null;cancel.IsEnabled=false;queue.IsEnabled=true;local.IsEnabled=remote.IsEnabled=replace.IsEnabled=true;UpdatePlan();}
    }
    async Task Cancel(){transfer?.Cancel();await manager.Cancel();}

    internal sealed class ExplorerPane : Grid
    {
        readonly Func<string,long,Task<FileMessage>> read;readonly Func<string,Task> mkdir;readonly Action changed;readonly Action<string> report;
        readonly TextBox address=new();readonly ListView list=new(){SelectionMode=SelectionMode.Extended};readonly ObservableCollection<Row> rows=new();readonly Stack<string> back=new(),forward=new();
        readonly Button more;bool loading;long next;public string CurrentPath {get;private set;}="";public bool IsLoading=>loading;
        public FileEntry[] SelectedFiles=>list.SelectedItems.Cast<Row>().Where(r=>!r.Entry.Directory).Select(r=>r.Entry).ToArray();
        internal int EntryCount=>rows.Count;
        internal void SelectFirstFile(){list.SelectedItem=rows.FirstOrDefault(r=>!r.Entry.Directory);}
        internal void Unselect(string path){var row=rows.FirstOrDefault(r=>r.Entry.Path==path);if(row is not null)list.SelectedItems.Remove(row);}
        internal void CheckTwoFiles()
        {
            UpdateLayout();foreach(var row in rows.Where(r=>!r.Entry.Directory).Take(2))
            {
                var container=(ListViewItem)list.ItemContainerGenerator.ContainerFromItem(row);var checkbox=FindCheck(container)??throw new Exception("File checkbox missing.");checkbox.SetCurrentValue(CheckBox.IsCheckedProperty,true);
            }
            if(SelectedFiles.Length!=2)throw new Exception("Multiple file checkboxes did not select rows.");
        }
        static CheckBox? FindCheck(DependencyObject parent)
        {
            if(parent is CheckBox check)return check;
            for(int i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++){var child=FindCheck(VisualTreeHelper.GetChild(parent,i));if(child is not null)return child;}return null;
        }
        public bool HasFile(string name)=>rows.Any(r=>r.Entry.Name.Equals(name,StringComparison.OrdinalIgnoreCase));
        public ExplorerPane(string title,Func<string,long,Task<FileMessage>> reader,Func<string,Task> create,Action selectionChanged,Action<string> reportError)
        {
            read=reader;mkdir=create;changed=selectionChanged;report=reportError;Background=Shade(18);Margin=new Thickness(0);RowDefinitions.Add(new(){Height=GridLength.Auto});RowDefinitions.Add(new(){Height=GridLength.Auto});RowDefinitions.Add(new());RowDefinitions.Add(new(){Height=GridLength.Auto});
            Children.Add(new TextBlock{Text="▣  "+title,FontWeight=FontWeights.SemiBold,Margin=new Thickness(10),FontSize=14});
            var bar=new DockPanel{Margin=new Thickness(6,0,6,6)};SetRow(bar,1);Children.Add(bar);
            var navigation=new StackPanel{Orientation=Orientation.Horizontal};DockPanel.SetDock(navigation,Dock.Left);bar.Children.Add(navigation);
            navigation.Children.Add(ActionButton("←",()=>_ = Back()));navigation.Children.Add(ActionButton("→",()=>_ = Forward()));navigation.Children.Add(ActionButton("↑",()=>_ = Go(CurrentPath.Length==0?"":RemotePath.Parent(CurrentPath))));
            var tools=new StackPanel{Orientation=Orientation.Horizontal};DockPanel.SetDock(tools,Dock.Right);bar.Children.Add(tools);tools.Children.Add(ActionButton("↻",()=>_ = Reload()));tools.Children.Add(ActionButton("▱ Nova pasta",()=>_ = NewFolder()));
            address.MinWidth=40;address.Background=Shade(30);address.Foreground=Brushes.White;address.Padding=new Thickness(6,8,6,8);address.Margin=new Thickness(0,0,4,6);bar.Children.Add(address);address.KeyDown+=async(_,e)=>{if(e.Key==Key.Enter){e.Handled=true;await Go(address.Text);}};
            list.ItemsSource=rows;list.Background=Shade(17);list.Foreground=Brushes.White;list.BorderBrush=Shade(38);ScrollViewer.SetHorizontalScrollBarVisibility(list,ScrollBarVisibility.Auto);
            var itemStyle=new Style(typeof(ListViewItem));itemStyle.Setters.Add(new Setter(HorizontalContentAlignmentProperty,HorizontalAlignment.Stretch));itemStyle.Setters.Add(new Setter(PaddingProperty,new Thickness(2,7,2,7)));itemStyle.Triggers.Add(new Trigger{Property=ListViewItem.IsSelectedProperty,Value=true,Setters={new Setter(BackgroundProperty,Blue),new Setter(ForegroundProperty,Brushes.White)}});list.ItemContainerStyle=itemStyle;
            var headerStyle=new Style(typeof(GridViewColumnHeader));headerStyle.Setters.Add(new Setter(BackgroundProperty,Shade(28)));headerStyle.Setters.Add(new Setter(ForegroundProperty,Brushes.LightGray));headerStyle.Setters.Add(new Setter(PaddingProperty,new Thickness(8,7,8,7)));
            var checkStyle=new Style(typeof(CheckBox));
            var box=new FrameworkElementFactory(typeof(Border));box.Name="SelectionBox";box.SetValue(WidthProperty,17d);box.SetValue(HeightProperty,17d);box.SetValue(Border.BorderBrushProperty,Brushes.Gray);box.SetValue(Border.BorderThicknessProperty,new Thickness(1));box.SetValue(Border.CornerRadiusProperty,new CornerRadius(2));
            var tick=new FrameworkElementFactory(typeof(TextBlock));tick.Name="SelectionTick";tick.SetValue(TextBlock.TextProperty,"✓");tick.SetValue(TextBlock.FontSizeProperty,12d);tick.SetValue(TextBlock.ForegroundProperty,Brushes.White);tick.SetValue(HorizontalAlignmentProperty,HorizontalAlignment.Center);tick.SetValue(VerticalAlignmentProperty,VerticalAlignment.Center);tick.SetValue(VisibilityProperty,Visibility.Collapsed);box.AppendChild(tick);
            var checkTemplate=new ControlTemplate(typeof(CheckBox)){VisualTree=box};checkTemplate.Triggers.Add(new Trigger{Property=CheckBox.IsCheckedProperty,Value=true,Setters={new Setter(BackgroundProperty,Blue,"SelectionBox"),new Setter(VisibilityProperty,Visibility.Visible,"SelectionTick")}});checkStyle.Setters.Add(new Setter(TemplateProperty,checkTemplate));
            var view=new GridView{ColumnHeaderContainerStyle=headerStyle};var checkbox=new FrameworkElementFactory(typeof(CheckBox));checkbox.SetValue(FocusableProperty,false);checkbox.SetValue(StyleProperty,checkStyle);checkbox.SetBinding(CheckBox.IsCheckedProperty,new Binding("IsSelected"){RelativeSource=new RelativeSource(RelativeSourceMode.FindAncestor,typeof(ListViewItem),1),Mode=BindingMode.TwoWay});
            view.Columns.Add(new(){Width=30,CellTemplate=new DataTemplate{VisualTree=checkbox}});
            foreach(var column in new[]{("Nome","Display",190d),("Tamanho","Length",75d),("Tipo","Type",90d),("Modificado","Modified",130d)})view.Columns.Add(new(){Header=column.Item1,DisplayMemberBinding=new Binding(column.Item2),Width=column.Item3});list.View=view;
            SetRow(list,2);Children.Add(list);list.SelectionChanged+=(_,_)=>changed();list.MouseDoubleClick+=async(_,e)=>{if(list.SelectedItem is Row row&&row.Entry.Directory){e.Handled=true;await Go(row.Entry.Path);}};
            more=ActionButton("Carregar mais",()=>_ = Load(CurrentPath,true));more.Visibility=Visibility.Collapsed;SetRow(more,3);Children.Add(more);
        }
        public async Task Go(string path)
        {
            if(loading)return;string previous=CurrentPath;if(await Load(path,false)){if(previous!=CurrentPath){back.Push(previous);forward.Clear();}}changed();
        }
        public Task Reload()=>Load(CurrentPath,false);
        async Task Back(){if(back.Count==0||loading)return;string path=back.Pop(),previous=CurrentPath;if(await Load(path,false))forward.Push(previous);else back.Push(path);}
        async Task Forward(){if(forward.Count==0||loading)return;string path=forward.Pop(),previous=CurrentPath;if(await Load(path,false))back.Push(previous);else forward.Push(path);}
        async Task<bool> Load(string path,bool append)
        {
            if(loading)return false;loading=true;changed();
            try{var response=await read(path,append?next:0);CurrentPath=response.Path??path;address.Text=CurrentPath;if(!append)rows.Clear();foreach(var entry in response.Entries??[])rows.Add(new(entry));next=response.Offset;more.Visibility=response.More?Visibility.Visible:Visibility.Collapsed;changed();return true;}
            catch(Exception ex){report(ex.Message);return false;}finally{loading=false;changed();}
        }
        async Task NewFolder()
        {
            if(CurrentPath.Length==0){report("Escolha uma unidade ou pasta primeiro.");return;}
            var prompt=new PromptWindow(Window.GetWindow(this)!,"Nova pasta","Nome da nova pasta:",value:"");if(prompt.ShowDialog()!=true)return;
            string name=prompt.TextValue.Trim();if(name.Length==0||name is "." or ".."||name.IndexOfAny(Path.GetInvalidFileNameChars())>=0){report("Nome de pasta inválido.");return;}
            try{await mkdir(RemotePath.Combine(CurrentPath,name));await Reload();}catch(Exception ex){report(ex.Message);}
        }
        public static string Size(long size)=>size>=1048576?$"{size/1048576d:N1} MB":size>=1024?$"{size/1024d:N1} KB":$"{size} B";
        sealed record Row(FileEntry Entry)
        {
            public string Display=>(Entry.Directory?"📁  ":"▤  ")+Entry.Name;
            public string Length=>Entry.Directory?"":Size(Entry.Size);
            public string Type=>Entry.Directory?"Pasta":Path.GetExtension(Entry.Name).TrimStart('.').ToUpperInvariant();
            public string Modified=>Entry.Modified?.ToLocalTime().ToString("dd/MM/yyyy HH:mm")??"";
        }
    }
}
