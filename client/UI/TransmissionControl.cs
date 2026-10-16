using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using RemoteDesk.Shared;
namespace RemoteDesk.Client.UI;
internal sealed class TransmissionControl : StackPanel
{
    internal Slider Balance {get;}
    internal int Level=>(int)Balance.Value;
    internal event Action<int>? Changed;
    readonly DispatcherTimer debounce=new(){Interval=TimeSpan.FromMilliseconds(300)};
    bool updating;
    internal void SetLevel(int level){updating=true;Balance.Value=level;updating=false;debounce.Stop();}
    internal TransmissionControl(int level)
    {
        Children.Add(new TextBlock{Text="Qualidade ↔ Velocidade",FontSize=14,Margin=new Thickness(0,0,0,8)});
        Balance=new Slider{Minimum=0,Maximum=100,Value=Math.Clamp(level,0,100),IsDirectionReversed=true,TickFrequency=5,IsSnapToTickEnabled=true,SmallChange=5,LargeChange=10,IsMoveToPointEnabled=true,Height=26,Margin=new Thickness(0,4,0,8),Focusable=true};
        Balance.Template=(ControlTemplate)System.Windows.Markup.XamlReader.Parse("""
        <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="Slider"><Grid Background="Transparent"><Border Height="4" Background="#555B65" CornerRadius="2" VerticalAlignment="Center"/><Track x:Name="PART_Track" IsDirectionReversed="{TemplateBinding IsDirectionReversed}"><Track.DecreaseRepeatButton><RepeatButton Command="Slider.DecreaseLarge" Opacity="0" Focusable="False"/></Track.DecreaseRepeatButton><Track.Thumb><Thumb Width="18" Height="18"><Thumb.Template><ControlTemplate TargetType="Thumb"><Ellipse Fill="#1D6FF2" Stroke="#FFFFFF" StrokeThickness="1.5"/></ControlTemplate></Thumb.Template></Thumb></Track.Thumb><Track.IncreaseRepeatButton><RepeatButton Command="Slider.IncreaseLarge" Opacity="0" Focusable="False"/></Track.IncreaseRepeatButton></Track></Grid></ControlTemplate>
        """);
        System.Windows.Automation.AutomationProperties.SetName(Balance,"Equilíbrio entre velocidade e qualidade");Children.Add(Balance);
        var labels=new DockPanel();var right=new TextBlock{Text="Velocidade",HorizontalAlignment=HorizontalAlignment.Right};DockPanel.SetDock(right,Dock.Right);labels.Children.Add(right);labels.Children.Add(new TextBlock{Text="Qualidade"});Children.Add(labels);
        var summary=new TextBlock{FontSize=12,Margin=new Thickness(0,8,0,4)};Children.Add(summary);
        void Refresh(){var p=TransmissionPreset.FromLevel(Level);summary.Text=$"JPEG {p.Quality}% · até {p.Width}px · até {p.Fps} FPS";}
        Refresh();Balance.ValueChanged+=(_,_)=>{Refresh();debounce.Stop();if(!updating)debounce.Start();};
        debounce.Tick+=(_,_)=>{debounce.Stop();Changed?.Invoke(Level);};Unloaded+=(_,_)=>debounce.Stop();
    }
}
