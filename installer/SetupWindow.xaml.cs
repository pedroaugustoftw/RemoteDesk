using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace RemoteDesk.Setup;
public partial class SetupWindow : Window
{
    private readonly string destination = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RemoteDesk");
    private bool loading = true;
    private bool edited;
    private bool existingConnection;
    private bool connectionPage;
    private bool installing;
    private bool completed;
    private readonly bool rendering = Environment.GetCommandLineArgs().Any(x => x.StartsWith("--render"));
    public SetupWindow()
    {
        InitializeComponent();
        RemoteDesk.Desktop.WindowSizing.Attach(this,680,650);
        Destination.Text = Path.Combine(destination, "app");
        ServerAddress.Text = "";
        SameCertificate.IsChecked = true;
        if (!rendering)
        {
            try
            {
                var settings = ClientConfiguration.Read(Path.Combine(destination, "settings.json"));
                string address = ClientConfiguration.Field(settings, "Server");
                existingConnection = !string.IsNullOrWhiteSpace(address);
                if (existingConnection) ServerAddress.Text = address;
                ServerFingerprint.Text = ClientConfiguration.Field(settings, "ServerCertificateSha256");
                RelayFingerprint.Text = ClientConfiguration.Field(settings, "RelayCertificateSha256");
                SameCertificate.IsChecked = ServerFingerprint.Text.Equals(RelayFingerprint.Text, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex) { Status.Text = ex.Message; Status.Foreground = Brushes.Salmon; Install.IsEnabled = false; }
        }
        RelayFingerprint.IsEnabled = SameCertificate.IsChecked != true;
        loading = false;
        Closing += (_, e) => { if (installing) { e.Cancel = true; Status.Text = "Aguarde o término da instalação antes de fechar."; } };
        if (rendering) Loaded += async (_, _) => await Render();
        if(Environment.GetCommandLineArgs().Contains("--layout-test"))Loaded+=async(_,_)=>await CheckLayout();
    }
    private void Configuration_Changed(object sender, TextChangedEventArgs e)
    {
        if (loading) return;
        edited = true;
        if (ReferenceEquals(sender, ServerFingerprint) && SameCertificate.IsChecked == true) RelayFingerprint.Text = ServerFingerprint.Text;
    }
    private void SameCertificate_Changed(object sender, RoutedEventArgs e)
    {
        if (RelayFingerprint == null) return;
        RelayFingerprint.IsEnabled = SameCertificate.IsChecked != true;
        if (SameCertificate.IsChecked == true) RelayFingerprint.Text = ServerFingerprint.Text;
        if (!loading) edited = true;
    }
    private void ShowConnection(bool value)
    {
        connectionPage = value;
        InstallationPage.Visibility = value ? Visibility.Collapsed : Visibility.Visible;
        ConnectionPage.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
        Back.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
        StepOne.Foreground = value ? new SolidColorBrush(Color.FromRgb(175, 175, 175)) : new SolidColorBrush(Color.FromRgb(230,230,230));
        StepTwo.Foreground = value ? new SolidColorBrush(Color.FromRgb(230,230,230)) : new SolidColorBrush(Color.FromRgb(175, 175, 175));
        Install.Content = value ? "Instalar" : "Continuar";
        Status.Text = value && existingConnection ? "Configuração existente carregada. Se não editar, ela será mantida sem alterações." : "O cliente iniciará na bandeja no próximo login se a opção estiver marcada.";
        Status.Foreground = new SolidColorBrush(Color.FromRgb(171, 171, 171));
        if (value) ServerAddress.Focus();
    }
    private void Back_Click(object sender, RoutedEventArgs e) => ShowConnection(false);
    private async Task CheckLayout()
    {
        try
        {
            MinWidth=0;MinHeight=0;Width=600;Height=420;ShowConnection(true);UpdateLayout();await System.Windows.Threading.Dispatcher.Yield();
            var root=(FrameworkElement)Content;
            var position=Install.TransformToAncestor(root).Transform(new Point(0,0));
            if(position.Y<0||position.Y+Install.ActualHeight>root.ActualHeight+1)throw new InvalidOperationException("Botão Instalar fora da janela.");
            if(PageScroll.ScrollableHeight<=0)throw new InvalidOperationException("Campos não disponibilizam rolagem.");
            PageScroll.ScrollToBottom();UpdateLayout();await System.Windows.Threading.Dispatcher.Yield();
            if(PageScroll.VerticalOffset<=0)throw new InvalidOperationException("Fim dos campos inacessível.");
            File.WriteAllText(Path.Combine(Path.GetTempPath(),"RemoteDesk-setup-layout-test.txt"),"PASS instalador 600x420; campos rolam; Instalar fixo e visível. Nenhuma instalação realizada.");Application.Current.Shutdown(0);
        }
        catch(Exception ex){File.WriteAllText(Path.Combine(Path.GetTempPath(),"RemoteDesk-setup-layout-test.txt"),"FAIL "+ex);Application.Current.Shutdown(1);}
    }
    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        if (completed) { Close(); return; }
        if (!connectionPage) { ShowConnection(true); return; }
        ConnectionConfiguration? configuration;
        try
        {
            // An unedited update leaves even formatting and unknown settings byte-for-byte intact.
            configuration = existingConnection && !edited ? null : ClientConfiguration.Validate(ServerAddress.Text, ServerFingerprint.Text, SameCertificate.IsChecked == true ? ServerFingerprint.Text : RelayFingerprint.Text);
            if (Process.GetProcessesByName("RemoteDesk").Length != 0) throw new InvalidOperationException("Feche o RemoteDesk e tente novamente.");
        }
        catch (Exception ex) { Status.Text = ex.Message; Status.Foreground = Brushes.Salmon; return; }
        installing = true;
        Install.IsEnabled = false; Back.IsEnabled = false; ConnectionPage.IsEnabled = false;
        Status.Text = "Preparando os arquivos do RemoteDesk…";
        Status.Foreground = new SolidColorBrush(Color.FromRgb(171, 171, 171));
        try
        {
            await Task.Run(() =>
            {
                using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("RemoteDesk.Payload.zip") ?? throw new InvalidOperationException("Payload ausente.");
                ClientConfiguration.Install(stream, destination, configuration);
                RemoteDesk.Desktop.InstalledApplication.Register(false);
            });
            CreateShortcut(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
            CreateShortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "RemoteDesk"));
            RemoteDesk.Client.Services.StartupRegistration.Set(StartWithWindows.IsChecked==true,Path.Combine(destination,"app","RemoteDesk.exe"));
            completed = true;
            Status.Text = "Instalação concluída. Abra pelo atalho agora; a inicialização automática vale para o próximo login.";
            Install.Content = "Concluir"; Back.Visibility = Visibility.Collapsed; Footer.Text = "PRONTO PARA CONECTAR";
        }
        catch (Exception ex) { Status.Text = "Não foi possível concluir: " + ex.Message; Status.Foreground = Brushes.Salmon; Back.IsEnabled = true; ConnectionPage.IsEnabled = true; }
        finally { installing = false; Install.IsEnabled = true; }
    }
    private void CreateShortcut(string directory)
    {
        Directory.CreateDirectory(directory);
        var type = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("Componente de atalhos indisponível.");
        dynamic shell = Activator.CreateInstance(type)!;
        dynamic shortcut = shell.CreateShortcut(Path.Combine(directory, "RemoteDesk.lnk"));
        try { shortcut.TargetPath = Path.Combine(destination, "app", "RemoteDesk.exe"); shortcut.WorkingDirectory = Path.Combine(destination, "app"); shortcut.Description = "RemoteDesk"; shortcut.Save(); }
        finally { Marshal.FinalReleaseComObject(shortcut); Marshal.FinalReleaseComObject(shell); }
    }
    private async Task Render()
    {
        try
        {
            var args = Environment.GetCommandLineArgs();
            int index = Array.FindIndex(args, x => x.StartsWith("--render"));
            ShowConnection(args[index] == "--render-connection");
            await Task.Delay(150); UpdateLayout();
            var bitmap = new RenderTargetBitmap((int)ActualWidth, (int)ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(this);
            string path = index + 1 < args.Length ? args[index + 1] : Path.Combine(Path.GetTempPath(), "RemoteDesk-client-installer.png");
            using var output = File.Create(path); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); encoder.Save(output);
            Application.Current.Shutdown(0);
        }
        catch { Application.Current.Shutdown(1); }
    }
}
