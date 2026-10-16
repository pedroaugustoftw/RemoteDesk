using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace RemoteDesk.ServerSetup;
public partial class App : Application
{
 protected override void OnStartup(StartupEventArgs e)
 {
  base.OnStartup(e);
  if(e.Args.Contains("--verify-payload")){try{using var zip=InstallerPayload.Open();foreach(var file in InstallerPayload.Validate(zip)){using var content=file.Entry.Open();_=System.Security.Cryptography.SHA256.HashData(content);}Shutdown(0);}catch{Shutdown(1);}return;}
  var window=new SetupWindow();MainWindow=window;
  if(e.Args.Contains("--ui-test"))
  {
    window.Loaded+=async(_,_)=>{try{await window.CheckInterface();Shutdown(0);}catch(Exception ex){Console.Error.WriteLine(ex);Shutdown(1);}};window.Show();return;
  }
  int ipArg=Array.IndexOf(e.Args,"--ip");if(ipArg>=0&&ipArg+1<e.Args.Length)window.SetIp(e.Args[ipArg+1]);
  if(e.Args.Contains("--render-network")){window.ShowNetworkPreview();}
  if(e.Args.Contains("--render-small")){window.Width=680;window.Height=560;}
  if(e.Args.Contains("--render")||e.Args.Contains("--render-small")||e.Args.Contains("--render-details")||e.Args.Contains("--render-network"))window.Loaded+=async(_,_)=>
  {
   await Task.Delay(350);if(e.Args.Contains("--render-details"))window.ShowDetails();window.UpdateLayout();
   var content=(FrameworkElement)window.Content;var bitmap=new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth),(int)Math.Ceiling(content.ActualHeight),96,96,PixelFormats.Pbgra32);bitmap.Render(content);
   var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
   string path=Environment.GetEnvironmentVariable("REMOTEDESK_SETUP_PREVIEW")??Path.Combine(Path.GetTempPath(),"RemoteDesk-Server-Setup.png");using(var stream=File.Create(path))encoder.Save(stream);Shutdown(0);
  };
  int resume=Array.IndexOf(e.Args,"--resume-action");
  if(resume>=0&&resume+1<e.Args.Length)window.Loaded+=(_,_)=>window.ResumeAction(e.Args[resume+1]);
  window.Show();
 }
}
