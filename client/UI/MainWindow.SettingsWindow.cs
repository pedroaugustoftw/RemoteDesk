using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
namespace RemoteDesk.Client.UI;
public partial class MainWindow
{
    private Window? settingsWindow;
    private void OpenSettingsWindow()
    {
        MenuOverlay.Visibility=Visibility.Collapsed;
        if(settingsWindow==null)
        {
            ((Panel)SettingsPage.Parent).Children.Remove(SettingsPage);
            Grid.SetColumn(SettingsPage,0);Grid.SetRow(SettingsPage,0);
            SettingsPage.Visibility=Visibility.Visible;
            SettingsSidebarColumn.Width=new GridLength(174);
            SettingsContentBorder.Padding=new Thickness(18,14,14,14);
            var body=new DockPanel{Background=new SolidColorBrush(Color.FromRgb(31,31,31))};
            var status=new TextBlock{Margin=new Thickness(14,8,14,10),FontSize=12,Foreground=Brushes.LightGray};
            status.SetBinding(TextBlock.TextProperty,new Binding("Text"){Source=Banner});
            DockPanel.SetDock(status,Dock.Bottom);body.Children.Add(status);body.Children.Add(SettingsPage);
            settingsWindow=new Window{Title="RemoteDesk — Configurações",Owner=this,Content=body,Width=760,Height=560,MinWidth=540,MinHeight=340,FontSize=13,Icon=Icon,WindowStartupLocation=WindowStartupLocation.CenterOwner};
            RemoteDesk.Desktop.WindowSizing.Attach(settingsWindow,760,560);
            settingsWindow.Closing+=(_,e)=>{if(!exiting){e.Cancel=true;settingsWindow.Hide();}};
        }
        SettingsPage.Visibility=Visibility.Visible;settingsWindow.Show();if(settingsWindow.WindowState==WindowState.Minimized)settingsWindow.WindowState=WindowState.Normal;settingsWindow.Activate();
    }
}
