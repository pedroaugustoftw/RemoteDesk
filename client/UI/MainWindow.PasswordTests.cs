using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
namespace RemoteDesk.Client.UI;
public partial class MainWindow
{
    private async Task CheckPasswordPersistence()
    {
        Settings_Click(this,new());PasswordSettings_Click(this,new());
        CheckBox enabled()=>SettingsContent.Children.OfType<CheckBox>().Single(c=>c.Name=="PasswordAccessEnabled");
        Button save()=>SettingsContent.Children.OfType<Button>().Single(c=>c.Name=="PasswordAccessSave");
        async Task Submit(){var button=save();button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));for(int i=0;i<300&&!button.IsEnabled;i++)await Task.Delay(10);if(!button.IsEnabled)throw new InvalidOperationException("Salvar acesso por senha não concluiu.");}
        enabled().IsChecked=true;foreach(var box in SettingsContent.Children.OfType<PasswordBox>())box.Password="short";await Submit();
        if(access.Read()!=null||!SettingsContent.Children.OfType<TextBlock>().Any(t=>t.Text.Contains("Não foi possível salvar")))throw new InvalidOperationException("Erro de senha curta não ficou explícito.");
        foreach(var box in SettingsContent.Children.OfType<PasswordBox>())box.Password="Local-UI-test-8739!";await Submit();
        if(access.Read()?.Enabled!=true)throw new InvalidOperationException("Política não persistiu.");
        UiSettings_Click(this,new());PasswordSettings_Click(this,new());
        if(enabled().IsChecked!=true||SettingsContent.Children.OfType<PasswordBox>().Any(p=>p.Password.Length!=0)||!SettingsContent.Children.OfType<TextBlock>().Any(t=>t.Text.Contains("Senha já cadastrada")))throw new InvalidOperationException("Estado ativo e senha cadastrada não reapareceram.");
        await Submit();if(access.Verify(new("ui","nonce","Local-UI-test-8739!"),"ui","nonce")==null)throw new InvalidOperationException("Salvar com campos vazios apagou a senha anterior.");
        enabled().IsChecked=false;await Submit();if(access.Read()!=null)throw new InvalidOperationException("Desativação não persistiu.");settingsWindow!.Hide();
    }
}
