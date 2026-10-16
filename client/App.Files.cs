using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using RemoteDesk.Client.FileTransfer;
using RemoteDesk.Client.Services;

namespace RemoteDesk.Client;
public partial class App
{
    async Task CheckFiles(bool render)
    {
        Window? owner=null;UI.FileTransferWindow? page=null;FileManager? viewer=null,host=null;
        try
        {
            string folder=Path.Combine(Profile.DirectoryPath,"file-ui-fixture");Directory.CreateDirectory(folder);
            Directory.CreateDirectory(Path.Combine(folder,"Documentos"));File.WriteAllText(Path.Combine(folder,"Planilha de teste.csv"),"teste,valor\nArquivo,1");File.WriteAllText(Path.Combine(folder,"Arquivo de teste.txt"),"RemoteDesk file browser test");
            viewer=new(e=>host!.Receive(e.File!,null),_=>{});host=new(e=>viewer.Receive(e.File!,null),_=>{});
            viewer.Start("ui-files");host.Start("ui-files");
            owner=new Window{Title="RemoteDesk — sessão de teste",Width=400,Height=200};MainWindow=owner;owner.Show();
            page=new(owner,"Sessão de teste",viewer,()=>{},()=>{});page.Show();
            await page.CheckInterface(folder);page.UpdateLayout();
            if(render)
            {
                var content=(FrameworkElement)page.Content;var bitmap=new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth),(int)Math.Ceiling(content.ActualHeight),96,96,PixelFormats.Pbgra32);bitmap.Render(content);
                var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using var output=File.Create(Path.Combine(Profile.DirectoryPath,"files-preview.png"));png.Save(output);
            }
            File.WriteAllText(Path.Combine(Profile.DirectoryPath,"files-tests.txt"),"PASS two explorers; real local and remote listing; selection and direction; page fits work area; file permission enforced by transport; host has no transfer launcher.");
            page.Close();owner.Close();viewer.Stop();host.Stop();Shutdown(0);
        }
        catch(Exception ex){File.WriteAllText(Path.Combine(Profile.DirectoryPath,"files-tests.txt"),"FAIL "+ex);page?.Close();owner?.Close();viewer?.Stop();host?.Stop();Shutdown(1);}
    }
}
