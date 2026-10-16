using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
namespace RemoteDesk.Client.UI;
internal sealed class ConnectionRequestWindow:Window
{
    readonly TextBlock status=new(){FontSize=13,Margin=new Thickness(0,12,0,12)};
    readonly StackPanel passwordPanel=new(){Visibility=Visibility.Collapsed};
    readonly PasswordBox password=new(){Padding=new Thickness(10),MaxLength=96};
    readonly CheckBox save=new(){Content="Salvar senha para este dispositivo",Margin=new Thickness(0,10,0,10)};
    readonly Button close=new(){Content="Cancelar",HorizontalAlignment=HorizontalAlignment.Right};
    readonly Button connect=new(){Content="Conectar",IsDefault=true,Visibility=Visibility.Collapsed};
    TaskCompletionSource<(string Password,bool Save)?>? answer;
    bool active=true;
    bool closed;
    internal event Action? CancelRequested;
    internal ConnectionRequestWindow(Window owner,string device)
    {
        Style=(Style)System.Windows.Application.Current.FindResource(typeof(Window));
        Owner=owner;Title="RemoteDesk — Conectar";Width=480;Height=360;MinWidth=340;MinHeight=240;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var body=new DockPanel();Content=new Border{Padding=new Thickness(20),Background=new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(31,31,31)),Child=body};
        var actions=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};actions.Children.Add(connect);actions.Children.Add(close);DockPanel.SetDock(actions,Dock.Bottom);body.Children.Add(actions);
        var content=new StackPanel();body.Children.Add(new ScrollViewer{Content=content,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
        content.Children.Add(new TextBlock{Text="Conectar a "+device,FontSize=20,FontWeight=FontWeights.SemiBold});content.Children.Add(status);
        content.Children.Add(passwordPanel);passwordPanel.Children.Add(new TextBlock{Text="Senha de acesso",Margin=new Thickness(0,0,0,6)});passwordPanel.Children.Add(password);passwordPanel.Children.Add(save);
        connect.Click+=(_,_)=>{if(password.Password.Length==0||System.Text.Encoding.UTF8.GetByteCount(password.Password)>96){status.Text="Digite uma senha de até 96 bytes UTF-8.";return;}var result=(password.Password,save.IsChecked==true);password.Clear();passwordPanel.Visibility=Visibility.Collapsed;connect.Visibility=Visibility.Collapsed;status.Text="Autenticando senha…";answer?.TrySetResult(result);};
        close.Click+=(_,_)=>Close();Closing+=(_,_)=>{answer?.TrySetResult(null);password.Clear();if(active){active=false;CancelRequested?.Invoke();}};
        Closed+=(_,_)=>closed=true;
        Waiting(false);RemoteDesk.Desktop.WindowSizing.Attach(this,480,360);
    }
    internal void Waiting(bool passwordMode)=>status.Text=passwordMode?"Verificando acesso por senha…":"Aguardando autorização do computador remoto…";
    internal Task<(string Password,bool Save)?> AskPassword()
    {
        answer=new(TaskCreationOptions.RunContinuationsAsynchronously);status.Text="Informe a senha configurada no computador de destino.";passwordPanel.Visibility=Visibility.Visible;connect.Visibility=Visibility.Visible;Activate();password.Focus();return answer.Task;
    }
    internal void Finish(string text){if(closed)return;active=false;answer?.TrySetResult(null);password.Clear();passwordPanel.Visibility=Visibility.Collapsed;connect.Visibility=Visibility.Collapsed;status.Text=text;close.Content="Fechar";}
    internal void Complete(){active=false;answer?.TrySetResult(null);if(!closed)Close();}
}
