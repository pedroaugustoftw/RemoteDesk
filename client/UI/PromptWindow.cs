using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using RemoteDesk.Shared;
namespace RemoteDesk.Client.UI;
public sealed class PromptWindow : Window
{
    public Permissions? Granted { get; private set; }
    public string TextValue { get; private set; } = "";
    public PromptWindow(Window owner, string title, string text, Permissions? request = null, string? value = null)
    {
        Style = (Style)System.Windows.Application.Current.FindResource(typeof(Window));
        Owner = owner; Title = "RemoteDesk — " + title; Width = 480; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        MaxHeight=Math.Max(200,SystemParameters.WorkArea.Height-40);
        var panel = new StackPanel { Margin = new Thickness(26) }; Content = new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
        panel.Children.Add(new TextBlock { Text = title, FontSize = 24, Foreground = new SolidColorBrush(Color.FromRgb(212,212,212)) });
        panel.Children.Add(new TextBlock { Text = text, Margin = new Thickness(0,18,0,18) });
        var view = new CheckBox { Content = "Permitir visualização da tela", IsChecked = request?.View, IsEnabled = request?.View == true };
        var control = new CheckBox { Content = "Permitir mouse e teclado", IsChecked = false, IsEnabled = request?.Control == true };
        var files = new CheckBox { Content = "Permitir navegar, enviar e receber arquivos", IsChecked = false, IsEnabled = request?.Files == true };
        var input = new TextBox { Text = value ?? "" };
        if (request != null) { panel.Children.Add(view); panel.Children.Add(control); panel.Children.Add(files); }
        if (value != null) panel.Children.Add(input);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0,20,0,0) };
        var no = new Button { Content = request == null ? "Cancelar" : "Recusar", IsCancel = true }; no.Click += (_,_) => { DialogResult = false; };
        var yes = new Button { Content = request == null ? "Salvar" : "Aceitar", IsDefault = request == null }; yes.Click += (_,_) => { if (request != null) { Granted = new(view.IsChecked == true, control.IsChecked == true && view.IsChecked == true, files.IsChecked == true); if (!Granted.View && !Granted.Files) return; } TextValue = input.Text; DialogResult = true; };
        buttons.Children.Add(no); buttons.Children.Add(yes); panel.Children.Add(buttons);
    }
}
