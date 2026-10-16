using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace RemoteDesk.Client;
public partial class App
{
    private async Task CheckIsland(bool render)
    {
        Directory.CreateDirectory(Services.Profile.DirectoryPath);
        UI.SessionWindow? session=null;
        try
        {
            session=new UI.SessionWindow("PC-ESCRITORIO",true,new(true,false,true),_=>throw new InvalidOperationException("Entrada não esperada."));
            int ended=0,files=0;session.EndRequested+=()=>ended++;session.SendFileRequested+=()=>files++;
            session.Show();await Task.Delay(150);session.UpdateLayout();var bar=session.Controls;
            if(!session.Topmost||session.ShowInTaskbar||session.ActualHeight>48||session.Width>360)throw new InvalidOperationException("Barra não está compacta e flutuante.");
            bar.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice,Environment.TickCount){RoutedEvent=Mouse.MouseEnterEvent});if(session.Opacity!=1)throw new InvalidOperationException("Hover não ganhou opacidade.");
            if(render)
            {
                var bitmap=new RenderTargetBitmap((int)Math.Ceiling(bar.ActualWidth),(int)Math.Ceiling(bar.ActualHeight),96,96,PixelFormats.Pbgra32);bitmap.Render(bar);
                var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using var output=File.Create(Path.Combine(Services.Profile.DirectoryPath,"island-preview.png"));png.Save(output);
            }
            bar.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice,Environment.TickCount){RoutedEvent=Mouse.MouseLeaveEvent});if(session.Opacity!=UI.SessionIsland.IdleOpacity)throw new InvalidOperationException("Sem hover não ficou transparente.");
            foreach(var button in new[]{bar.DetailsButton,bar.MinimizeButton,bar.RestoreButton,bar.EndButton})if(button.Content is not Viewbox)throw new InvalidOperationException("Botão sem ícone vetorial.");
            void Snapshot(string name){if(!render)return;session.UpdateLayout();var bitmap=new RenderTargetBitmap((int)Math.Ceiling(bar.ActualWidth),(int)Math.Ceiling(bar.ActualHeight),96,96,PixelFormats.Pbgra32);bitmap.Render(bar);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using var output=File.Create(Path.Combine(Services.Profile.DirectoryPath,name));png.Save(output);}
            bar.MinimizeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));session.UpdateLayout();await Task.Delay(100);
            if(!bar.IsMinimized||bar.Expanded||session.Width!=180||session.ActualHeight>40||bar.EndButton.Visibility!=Visibility.Visible||bar.RestoreButton.Visibility!=Visibility.Visible||ended!=0)throw new InvalidOperationException("Minimizar deve recolher a barra sem encerrar e preservar restaurar/encerrar.");Snapshot("island-minimized.png");
            bar.RestoreButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));session.UpdateLayout();if(bar.IsMinimized||session.Width!=360)throw new InvalidOperationException("Restaurar não recuperou a barra.");
            if(bar.QualityControl==null)throw new InvalidOperationException("Controle de qualidade ausente.");int level=-1;session.QualityChanged+=value=>level=value;bar.SetExpanded(true);bar.QualityControl.Balance.Value=100;await Task.Delay(400);if(level!=100)throw new InvalidOperationException("Qualidade não roteada.");bar.SetExpanded(false);
            bar.EndButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));if(files!=0||ended!=1)throw new InvalidOperationException("Host deve exibir apenas encerramento, sem iniciar arquivos.");
            bar.DetailsButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));session.UpdateLayout();if(!bar.Expanded||session.ActualHeight<=100)throw new InvalidOperationException("Detalhes não expandiram.");Snapshot("island-expanded.png");
            bar.MinimizeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));session.UpdateLayout();if(!bar.IsMinimized||bar.Expanded||session.ActualHeight>40)throw new InvalidOperationException("Minimizar com detalhes abertos falhou.");bar.RestoreButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));session.UpdateLayout();
            await Task.Delay(1100);if(bar.Clock.Text=="00:00:00")throw new InvalidOperationException("Temporizador parado.");
            var pointer=new UI.RemotePointerWindow(session);pointer.Move(new("move",.5,.5),new System.Drawing.Rectangle(0,0,400,300));if(!pointer.IsVisible||pointer.IsHitTestVisible||pointer.ShowInTaskbar)throw new InvalidOperationException("Ponteiro remoto deve ser visível e não interativo.");pointer.Close();
            session.Ending=true;session.Close();
            session=new UI.SessionWindow("Somente visualização",false,new(true,false,false),_=>{});int viewerEnded=0;session.EndRequested+=()=>viewerEnded++;session.Show();await Task.Delay(100);if(session.HasFloatingControls||session.OwnedWindows.Count!=0)throw new InvalidOperationException("Viewer não deve ter barra flutuante.");if(session.ViewerFileButton?.Visibility!=Visibility.Collapsed)throw new InvalidOperationException("Arquivo exibido sem permissão.");session.Close();if(viewerEnded!=1)throw new InvalidOperationException("Encerramento do viewer não foi único.");session=null;
            session=new UI.SessionWindow("605077841",false,new(true,true,true),_=>{});session.Show();if(!session.HasBluePointer||session.HasFloatingControls)throw new InvalidOperationException("Ponteiro azul ausente ou barra flutuante indevida.");
            using(var bitmap=new System.Drawing.Bitmap(1280,720)){using(var graphics=System.Drawing.Graphics.FromImage(bitmap)){graphics.Clear(System.Drawing.Color.FromArgb(12,17,23));using var blue=new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(29,111,242));graphics.FillRectangle(blue,80,100,440,260);using var font=new System.Drawing.Font("Segoe UI",24);graphics.DrawString("RemoteDesk — imagem de teste",font,System.Drawing.Brushes.White,80,40);graphics.DrawRectangle(System.Drawing.Pens.Gray,560,100,640,440);}using var encoded=new MemoryStream();bitmap.Save(encoded,System.Drawing.Imaging.ImageFormat.Jpeg);session.Frame(Convert.ToBase64String(encoded.ToArray()));}
            session.ViewerQualityButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));if(!session.ViewerSettingsVisible)throw new InvalidOperationException("Painel de qualidade não abriu.");session.ViewerQualityButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            session.ViewerFitButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));if(session.FitImage)throw new InvalidOperationException("Tamanho original não foi ativado.");session.ViewerFitButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));if(!session.FitImage)throw new InvalidOperationException("Ajuste da imagem não voltou.");
            session.ProbeRequested+=value=>session.ReceiveProbe(value);await Task.Delay(1100);session.UpdateLayout();
            if(render){var bitmap=new RenderTargetBitmap((int)Math.Ceiling(session.ActualWidth),(int)Math.Ceiling(session.ActualHeight),96,96,PixelFormats.Pbgra32);bitmap.Render(session);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using var output=File.Create(Path.Combine(Services.Profile.DirectoryPath,"viewer-preview.png"));png.Save(output);}
            int viewerFile=0,viewerEnd=0;session.SendFileRequested+=()=>viewerFile++;session.EndRequested+=()=>viewerEnd++;session.ViewerFileButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));session.ViewerEndButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));if(viewerFile!=1||viewerEnd!=1)throw new InvalidOperationException("Ações da nova janela de sessão não roteadas.");session.Ending=true;session.Close();session=null;
            var restart=Services.AdministratorMode.RestartInfo();if(restart.Verb!="runas"||!restart.UseShellExecute||!restart.ArgumentList.Contains("--wait-for-parent"))throw new InvalidOperationException("Elevação deve usar UAC e aguardar liberação da instância anterior.");
            File.WriteAllText(Path.Combine(Services.Profile.DirectoryPath,"island-tests.txt"),"PASS barra 360px e até 48px de altura; ícones vetoriais; minimizar para 180px e até 40px de altura; restaurar; minimizar detalhes abertos; indicador/encerrar preservados sem interromper sessão; hover/transparência; timer; arquivo/qualidade; barra somente no host; viewer integrado; ponteiro azul; limpeza; reinício preparado via UAC. Nenhuma elevação ou entrada real executada.");Shutdown(0);
        }
        catch(Exception ex){if(session!=null){session.Ending=true;session.Close();}File.WriteAllText(Path.Combine(Services.Profile.DirectoryPath,"island-tests.txt"),"FAIL "+ex);Shutdown(1);}
    }
}
