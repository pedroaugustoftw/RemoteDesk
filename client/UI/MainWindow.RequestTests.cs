using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.IO;
namespace RemoteDesk.Client.UI;
public partial class MainWindow
{
    private async Task CheckRequestWindow()
    {
        static IEnumerable<T> Find<T>(DependencyObject parent)where T:DependencyObject
        {
            foreach(var child in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>()){if(child is T match)yield return match;foreach(var nested in Find<T>(child))yield return nested;}
        }
        var dialog=new ConnectionRequestWindow(this,"123 456 789");int cancellations=0;dialog.CancelRequested+=()=>cancellations++;dialog.Show();dialog.UpdateLayout();
        if(!Find<TextBlock>(dialog).Any(t=>t.Text.StartsWith("Aguardando autorização")))throw new InvalidOperationException("Espera por autorização ausente.");
        var answer=dialog.AskPassword();var password=Find<PasswordBox>(dialog).Single();password.Password="Test-password-8392!";var save=Find<CheckBox>(dialog).Single();save.IsChecked=true;dialog.UpdateLayout();
        var content=(FrameworkElement)dialog.Content;var image=new RenderTargetBitmap((int)content.ActualWidth,(int)content.ActualHeight,96,96,PixelFormats.Pbgra32);image.Render(content);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(image));using(var output=File.Create(Path.Combine(Services.Profile.DirectoryPath,"request-password-preview.png")))png.Save(output);
        Find<Button>(dialog).Single(b=>Equals(b.Content,"Conectar")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));var result=await answer;
        if(result?.Password!="Test-password-8392!"||result?.Save!=true||password.Password.Length!=0)throw new InvalidOperationException("Senha e opção salvar não roteadas ou senha permaneceu no campo.");
        dialog.Finish("Conexão recusada pelo computador remoto.");if(!Find<TextBlock>(dialog).Any(t=>t.Text=="Conexão recusada pelo computador remoto."))throw new InvalidOperationException("Recusa não exibida.");dialog.Complete();if(cancellations!=0)throw new InvalidOperationException("Fechamento programático cancelou sessão.");
        dialog=new ConnectionRequestWindow(this,"Teste");dialog.CancelRequested+=()=>cancellations++;dialog.Show();var canceled=dialog.AskPassword();dialog.Close();if(await canceled!=null||cancellations!=1)throw new InvalidOperationException("Cancelamento não completou entrada de senha.");
    }
}
