using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using RemoteDesk.Client.Services;
namespace RemoteDesk.Client.UI;
public partial class MainWindow
{
    private void RestartAsAdministrator()
    {
        if(AdministratorMode.IsElevated){Banner.Text="RemoteDesk já está executando como administrador.";return;}
        if(MessageBox.Show(settingsWindow??this,"O RemoteDesk será reiniciado e as conexões atuais serão encerradas. Confirme a solicitação do Windows neste computador para controlar aplicativos elevados. Continuar?","RemoteDesk — Administrador",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)return;
        try{if(Process.Start(AdministratorMode.RestartInfo())==null)throw new InvalidOperationException("Não foi possível iniciar o modo administrador.");exiting=true;Close();}
        catch(Win32Exception ex)when(ex.NativeErrorCode==1223){Banner.Text="Elevação cancelada. RemoteDesk continua sem alteração.";}
        catch(Exception ex){Banner.Text="Não foi possível elevar: "+ex.Message;}
    }
}
