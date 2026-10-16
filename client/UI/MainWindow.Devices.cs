using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using RemoteDesk.Client.Security;
using RemoteDesk.Client.Services;
using RemoteDesk.Shared;
namespace RemoteDesk.Client.UI;
public partial class MainWindow
{
    readonly PasswordAccess access=new(Profile.DirectoryPath);
    readonly Dictionary<string,DevicePresence> presence=new();DateTime presenceAt;
    string? challengeNonce;DateTime challengeAt;
    SavedDevice? authenticatingDevice;string? newSavedPassword;string? authenticatingKey;
    PasswordPrompt? passwordPrompt;
    ConnectionRequestWindow? connectionRequest;
    TaskCompletionSource<bool>? policyAcknowledgement;
    string? policyRequest;
    bool publishedPasswordAccess;
    bool serverPasswordSupported;
    bool serverCapabilityChecked;
    private async Task<bool> PublishAccessPolicy()
    {
        if(!connection.Ready)return false;
        policyAcknowledgement?.TrySetResult(false);var completion=policyAcknowledgement=new(TaskCreationOptions.RunContinuationsAsynchronously);policyRequest=Guid.NewGuid().ToString("N");publishedPasswordAccess=access.Read()?.Enabled==true;
        try{await connection.Send(new(){Type="access-policy",RequestId=policyRequest,PasswordAccess=publishedPasswordAccess});return await completion.Task.WaitAsync(TimeSpan.FromSeconds(3));}
        catch(Exception ex)when(ex is TimeoutException or IOException or OperationCanceledException or System.Net.WebSockets.WebSocketException){if(publishedPasswordAccess)Banner.Text="Acesso por senha salvo, mas o servidor não confirmou. Atualize o servidor RemoteDesk.";return false;}
        finally{serverCapabilityChecked=true;}
    }
    private void ShowConnectionRequest(Envelope message)
    {
        connectionRequest?.Complete();connectionRequest=new ConnectionRequestWindow(this,FormatId(message.DeviceId??Address.Text));
        connectionRequest.CancelRequested+=async()=>await EndSession();connectionRequest.Waiting(message.PasswordAccess);connectionRequest.Show();
    }
    private async Task RefreshPresence()
    {
        if(!connection.Ready||profile.Devices.Count==0||serverCapabilityChecked&&!serverPasswordSupported)return;
        try{await connection.Send(new(){Type="presence",Payload=JsonSerializer.Serialize(profile.Devices.Select(d=>d.Id).Take(200).ToArray(),Wire.Json)});}catch{presence.Clear();}
    }
    private void ReceivePresence(Envelope message)
    {
        if(message.Payload is not {Length:<=32768})throw new IOException("Lista de status inválida.");
        var list=JsonSerializer.Deserialize<DevicePresence[]>(message.Payload,Wire.Json)??[];if(list.Length>200)throw new IOException("Lista de status excedida.");
        presence.Clear();foreach(var item in list.Where(d=>profile.Devices.Any(saved=>saved.Id==d.Id)))presence[item.Id]=item;presenceAt=DateTime.UtcNow;RefreshDeviceViews();
    }
    private string DeviceStatusLabel(string id)=>!connection.Ready||DateTime.UtcNow-presenceAt>TimeSpan.FromSeconds(30)||!presence.TryGetValue(id,out var status)?"Status indisponível":status.Online?"Online":"Offline";
    private async void AddDevice_Click(object sender,RoutedEventArgs e)
    {
        var editor=new DeviceEditor(this);if(editor.ShowDialog()!=true)return;
        if(profile.Devices.Count>=200||profile.Devices.Any(d=>d.Id==editor.DeviceId)){Banner.Text="Dispositivo já cadastrado ou limite de 200 atingido.";return;}
        profile.Devices.Add(new(editor.DeviceId,string.IsNullOrWhiteSpace(editor.Alias)?FormatId(editor.DeviceId):editor.Alias,editor.UsePassword,editor.UsePassword&&editor.SavePassword?PasswordAccess.Seal(editor.Password):""));profile.Save();ShowDevices("found");await RefreshPresence();
    }
    private void EditDevice(SavedDevice device)
    {
        var editor=new DeviceEditor(this,device);if(editor.ShowDialog()!=true)return;
        string secret=editor.UsePassword&&editor.SavePassword?(editor.Password.Length>0?PasswordAccess.Seal(editor.Password):device.PasswordDpapi):"";
        profile.Devices[profile.Devices.IndexOf(device)]=device with{Name=string.IsNullOrWhiteSpace(editor.Alias)?device.Id:editor.Alias,PasswordAccess=editor.UsePassword,PasswordDpapi=secret};profile.Save();RefreshDeviceViews();
    }
    private void DeleteDevice(SavedDevice device)
    {
        if(MessageBox.Show(this,"Remover "+device.Name+" e sua senha salva?","RemoteDesk",MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;
        profile.Devices.Remove(device);profile.Save();RefreshDeviceViews();
    }
    private async void ConnectPassword_Click(object sender,RoutedEventArgs e)
    {
        var device=Selected();if(device!=null)Address.Text=device.Id;await RequestConnection(true);
    }
    private async Task BeginPasswordChallenge(Envelope message)
    {
        if(!access.Available){await connection.Send(new(){Type="reject",SessionId=message.SessionId});pendingId=null;Banner.Text="Acesso por senha desativado ou temporariamente bloqueado.";return;}
        challengeNonce=Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));challengeAt=DateTime.UtcNow;
        await connection.Send(new(){Type="password-challenge",SessionId=message.SessionId,Nonce=challengeNonce});Banner.Text="Autenticando acesso por senha…";
    }
    private async Task SendPasswordProof(Envelope message)
    {
        if(host||pendingId!=message.SessionId||message.Nonce?.Length!=44||message.PublicKey is null)return;
        string hash=Convert.ToHexString(SHA256.HashData(Convert.FromBase64String(message.PublicKey)));
        if(!string.IsNullOrEmpty(authenticatingDevice?.KeySha256)&&authenticatingDevice.KeySha256!=hash){await EndSession();Banner.Text="A chave do dispositivo mudou. Remova e cadastre novamente após conferir o destino.";connectionRequest?.Finish(Banner.Text);return;}
        string password;
        if(!string.IsNullOrEmpty(authenticatingDevice?.PasswordDpapi))password=PasswordAccess.Unseal(authenticatingDevice.PasswordDpapi);
        else
        {
            RestoreFromTray();if(connectionRequest==null)ShowConnectionRequest(message);
            var result=await connectionRequest!.AskPassword();if(result is null||pendingId!=message.SessionId)return;
            password=result.Value.Password;if(result.Value.Save)newSavedPassword=PasswordAccess.Seal(password);
        }
        authenticatingKey=hash;
        var proof=PasswordAccess.EncryptProof(message.PublicKey,new(message.SessionId!,message.Nonce,password));
        await connection.Send(new(){Type="password-proof",SessionId=message.SessionId,Payload=proof});
    }
    private async Task VerifyPasswordProof(Envelope message)
    {
        if(!host||pendingId!=message.SessionId||challengeNonce is null)return;
        string nonce=challengeNonce;challengeNonce=null;
        Permissions? permitted=null;
        if(DateTime.UtcNow-challengeAt<TimeSpan.FromSeconds(30))
        {
            try{var proof=connection.DecryptProof(message.Payload??"");permitted=await Task.Run(()=>access.Verify(proof,message.SessionId!,nonce));}
            catch(Exception ex)when(ex is CryptographicException or FormatException or JsonException or ArgumentException){access.InvalidAttempt();}
        }
        if(pendingId!=message.SessionId)return;
        if(permitted is null){await connection.Send(new(){Type="reject",SessionId=message.SessionId});pendingId=null;Banner.Text="Acesso por senha recusado.";Profile.Log("password-access rejected");return;}
        var requested=message.Permissions??pendingPasswordPermissions;
        var grantedPassword=new Permissions(permitted.View&&requested.View,permitted.Control&&requested.Control,permitted.Files&&requested.Files);
        // Explicit opt-in on this host is the authorization for this authenticated session.
        await connection.Send(new(){Type="respond",SessionId=message.SessionId,Permissions=grantedPassword});Profile.Log("password-access authenticated");
    }
    Permissions pendingPasswordPermissions=new();
    private void CommitPasswordDevice()
    {
        if(host||authenticatingKey is null)return;
        var device=authenticatingDevice??new SavedDevice(Address.Text.Replace(" ","").Trim(),Address.Text.Trim(),true);
        var updated=device with{PasswordAccess=true,PasswordDpapi=newSavedPassword??device.PasswordDpapi,KeySha256=authenticatingKey};
        profile.Devices.RemoveAll(d=>d.Id==device.Id);profile.Devices.Add(updated);profile.Save();newSavedPassword=null;
    }
    private void PasswordSettings_Click(object sender,RoutedEventArgs e)
    {
        Heading("Acesso por senha");var current=access.Read();
        var status=new TextBlock{Name="PasswordAccessStatus",Text=current?.Enabled==true?"Acesso por senha ativo · Senha já cadastrada.":"Acesso por senha desativado · Nenhuma senha cadastrada.",Foreground=current?.Enabled==true?Brushes.LightGreen:Brushes.LightGray,Margin=new Thickness(0,4,0,12)};SettingsContent.Children.Add(status);
        var enabled=new CheckBox{Name="PasswordAccessEnabled",Content="Permitir acesso com senha sem confirmação local",IsChecked=current?.Enabled==true};SettingsContent.Children.Add(enabled);
        SettingsContent.Children.Add(new TextBlock{Text="Configure neste computador de destino, na conta Windows "+Environment.UserName+". Cada sessão mantém o indicador visível e pode ser encerrada imediatamente. O cliente deve estar aberto na bandeja e o usuário Windows conectado.",Margin=new Thickness(0,12,0,12)});
        SettingsContent.Children.Add(new TextBlock{Text="Nova senha (mínimo de 12 caracteres)"});var password=new PasswordBox{Name="AccessNewPassword",Padding=new Thickness(10),Margin=new Thickness(0,8,0,12)};SettingsContent.Children.Add(password);
        SettingsContent.Children.Add(new TextBlock{Text="Confirmar senha"});var confirm=new PasswordBox{Name="AccessConfirmPassword",Padding=new Thickness(10),Margin=new Thickness(0,8,0,12)};SettingsContent.Children.Add(confirm);
        SettingsContent.Children.Add(new TextBlock{Text="Os campos ficam vazios ao reabrir: a senha cadastrada permanece protegida. Para manter a senha existente, deixe os dois campos vazios.",FontSize=12,Margin=new Thickness(0,0,0,12)});
        var control=new CheckBox{Content="Permitir mouse e teclado por senha",IsChecked=current?.Permissions.Control==true};var filesPermission=new CheckBox{Content="Permitir transferência de arquivos por senha",IsChecked=current?.Permissions.Files==true};SettingsContent.Children.Add(control);SettingsContent.Children.Add(filesPermission);
        var save=new Button{Name="PasswordAccessSave",Content="Salvar",HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,18,0,0)};SettingsContent.Children.Add(save);
        save.Click+=async(_,_)=>{
            save.IsEnabled=false;
            try{
                if(sessionId!=null||pendingId!=null)throw new InvalidOperationException("Encerre a sessão antes de alterar o acesso por senha.");
                var permissions=new Permissions(true,control.IsChecked==true,filesPermission.IsChecked==true);
                if(enabled.IsChecked!=true)access.Disable();
                else if(password.Password.Length==0&&confirm.Password.Length==0&&access.Read()!=null)access.UpdatePermissions(permissions);
                else{if(password.Password!=confirm.Password)throw new ArgumentException("As senhas não coincidem.");var secret=password.Password;await Task.Run(()=>access.Configure(secret,permissions));}
                var stored=access.Read();enabled.IsChecked=stored?.Enabled==true;password.Clear();confirm.Clear();bool connected=connection.Ready;bool confirmed=await PublishAccessPolicy();
                status.Text=stored?.Enabled==true?"Acesso por senha ativo · Senha cadastrada e salva neste computador.":"Acesso por senha desativado.";
                if(stored?.Enabled==true&&!confirmed)status.Text+=connected?" O servidor não confirmou: atualize o servidor antes de conectar.":" Aguardando conexão com o servidor para publicar a configuração.";
                status.Foreground=stored?.Enabled==true&&connected&&!confirmed?Brushes.Orange:Brushes.LightGreen;Banner.Text=status.Text;
            }catch(Exception ex){status.Text="Não foi possível salvar: "+ex.Message;status.Foreground=Brushes.Salmon;Banner.Text=status.Text;}
            finally{save.IsEnabled=true;status.BringIntoView();}
        };
    }
}
