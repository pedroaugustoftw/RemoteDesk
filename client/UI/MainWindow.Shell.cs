using System.Windows;
using System.Windows.Controls;
namespace RemoteDesk.Client.UI;
public partial class MainWindow
{
    private void Minimize_Click(object sender,RoutedEventArgs e)=>WindowState=WindowState.Minimized;
    private void Maximize_Click(object sender,RoutedEventArgs e){if(WindowState==WindowState.Maximized)SystemCommands.RestoreWindow(this);else SystemCommands.MaximizeWindow(this);}
    private void Close_Click(object sender,RoutedEventArgs e)=>Close();
    private void AddFavorite_Click(object sender,RoutedEventArgs e)
    {
        var prompt=new PromptWindow(this,"Adicionar favorito","Informe o ID RemoteDesk do dispositivo.",value:"");if(prompt.ShowDialog()!=true)return;
        string id=prompt.TextValue.Replace(" ","").Trim();
        if(id.Length!=9||id.Any(c=>!char.IsAsciiDigit(c))){Banner.Text="Use um ID de nove dígitos.";return;}
        if(profile.Favorites.Any(d=>d.Id==id)){Banner.Text="Este dispositivo já está nos favoritos.";return;}
        profile.Favorites.Add(profile.Recent.FirstOrDefault(d=>d.Id==id)??new(id,FormatId(id),DateTime.UtcNow));profile.Save();RefreshDeviceViews();
    }
    private void ArrangeConnectionCards()
    {
        MainSidebarColumn.Width=new GridLength(ActualWidth<1000?185:210);
        RecentTab.Content=ActualWidth<1000?"⇄    Recentes":"⇄    Sessões Recentes";
        bool stacked=ActualWidth<1000;
        Grid.SetColumnSpan(LocalCard,stacked?3:1);Grid.SetColumn(RemoteCard,stacked?0:2);
        Grid.SetRow(RemoteCard,stacked?1:0);Grid.SetColumnSpan(RemoteCard,stacked?3:1);
    }
    private void ShowFileTransfer()
    {
        if(host||sessionId is null||!granted.Files||sessionWindow is null)return;
        if(fileWindow is not null){fileWindow.WindowState=WindowState.Normal;fileWindow.Activate();return;}
        fileWindow=new FileTransferWindow(sessionWindow,sessionWindow.Title.Replace("RemoteDesk — ",""),files,()=>{RestoreFromTray();Recent_Click(this,new());},()=>Settings_Click(this,new()));
        fileWindow.Closed+=(_,_)=>fileWindow=null;fileWindow.Show();
    }
    private void Transfer_Click(object sender,RoutedEventArgs e)
    {
        if(sessionId!=null&&host){Banner.Text="A página de arquivos fica no computador que está acessando esta sessão.";return;}
        if(sessionId!=null&&granted.Files&&sessionWindow!=null){ShowFileTransfer();return;}
        if(sessionId!=null){Banner.Text="O destino não autorizou transferência de arquivos nesta sessão.";return;}
        News_Click(sender,e);Address.Focus();
        if(string.IsNullOrWhiteSpace(Address.Text)){Banner.Text="Informe o ID remoto e conecte. O destino precisa autorizar os arquivos.";return;}
        openFilesOnConnect=true;Connect_Click(sender,e);Banner.Text="Solicitando sessão. Após a autorização, use Enviar arquivo na barra flutuante.";
    }
}
