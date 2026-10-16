using System.Windows;
using System.Windows.Controls;
using RemoteDesk.Client.Services;
namespace RemoteDesk.Client.UI;
internal sealed class DeviceEditor:Window
{
    readonly TextBox id=new(),name=new();readonly PasswordBox password=new(){MaxLength=96};
    readonly CheckBox usePassword=new(){Content="Conectar usando senha do destino"};
    readonly CheckBox save=new(){Content="Salvar senha neste usuário Windows"};
    public string DeviceId=>id.Text.Replace(" ","").Trim();public string Alias=>name.Text.Trim();
    public string Password=>password.Password;public bool SavePassword=>save.IsChecked==true;public bool UsePassword=>usePassword.IsChecked==true;
    public DeviceEditor(Window owner,SavedDevice? existing=null)
    {
        Owner=owner;Title=existing==null?"RemoteDesk — Adicionar dispositivo":"RemoteDesk — Editar dispositivo";Width=480;Height=510;MinHeight=300;MinWidth=340;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        RemoteDesk.Desktop.WindowSizing.Attach(this,480,510);
        var root=new DockPanel{Margin=new Thickness(22)};Content=root;var actions=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};DockPanel.SetDock(actions,Dock.Bottom);root.Children.Add(actions);
        var cancel=new Button{Content="Cancelar",IsCancel=true};cancel.Click+=(_,_)=>DialogResult=false;actions.Children.Add(cancel);
        var ok=new Button{Content="Salvar",IsDefault=true};ok.Click+=(_,_)=>{
            if(DeviceId.Length!=9||DeviceId.Any(c=>!char.IsAsciiDigit(c))||Alias.Length>80){MessageBox.Show(this,"Informe ID de nove dígitos e apelido de até 80 caracteres.","RemoteDesk");return;}
            if(System.Text.Encoding.UTF8.GetByteCount(Password)>96){MessageBox.Show(this,"A senha é grande demais. Use uma senha mais curta.","RemoteDesk");return;}
            if(SavePassword&&UsePassword&&Password.Length==0&&string.IsNullOrEmpty(existing?.PasswordDpapi)){MessageBox.Show(this,"Digite a senha para salvá-la.","RemoteDesk");return;}
            DialogResult=true;
        };actions.Children.Add(ok);
        var fields=new StackPanel();root.Children.Add(new ScrollViewer{Content=fields,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
        fields.Children.Add(new TextBlock{Text="Dispositivo",FontSize=24,FontWeight=FontWeights.SemiBold});
        fields.Children.Add(new TextBlock{Text="Identificador remoto",Margin=new Thickness(0,20,0,8)});fields.Children.Add(id);
        fields.Children.Add(new TextBlock{Text="Apelido",Margin=new Thickness(0,14,0,8)});fields.Children.Add(name);
        fields.Children.Add(usePassword);fields.Children.Add(new TextBlock{Text="Senha de acesso",Margin=new Thickness(0,8,0,8)});password.Padding=new Thickness(10);fields.Children.Add(password);fields.Children.Add(save);
        fields.Children.Add(new TextBlock{Text="A senha é configurada nas Configurações > Acesso do computador de destino. Se não salvar, ela será pedida a cada conexão. Ao editar, deixe vazia para manter a senha já salva.",FontSize=12,Margin=new Thickness(0,8,0,12)});
        id.Text=existing?.Id??"";name.Text=existing?.Name??"";id.IsReadOnly=existing!=null;usePassword.IsChecked=existing?.PasswordAccess??true;save.IsChecked=!string.IsNullOrEmpty(existing?.PasswordDpapi);
    }
}
internal sealed class PasswordPrompt:Window
{
    readonly PasswordBox input=new(){Padding=new Thickness(10),MaxLength=96};readonly CheckBox save=new(){Content="Salvar senha para este dispositivo"};
    public string Password=>input.Password;public bool SavePassword=>save.IsChecked==true;
    public PasswordPrompt(Window owner,string device)
    {
        Owner=owner;Title="RemoteDesk — Autenticação por senha";Width=430;Height=280;WindowStartupLocation=WindowStartupLocation.CenterOwner;ResizeMode=ResizeMode.NoResize;
        var root=new StackPanel{Margin=new Thickness(22)};Content=root;root.Children.Add(new TextBlock{Text="Senha para "+device,FontSize=20});root.Children.Add(input);root.Children.Add(save);
        var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};root.Children.Add(buttons);
        var no=new Button{Content="Cancelar",IsCancel=true};no.Click+=(_,_)=>DialogResult=false;buttons.Children.Add(no);
        var yes=new Button{Content="Conectar",IsDefault=true};yes.Click+=(_,_)=>{if(Password.Length>0)DialogResult=true;};buttons.Children.Add(yes);
        Loaded+=(_,_)=>input.Focus();
    }
}
