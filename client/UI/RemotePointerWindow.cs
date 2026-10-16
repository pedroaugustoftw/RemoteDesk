using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using RemoteDesk.Shared;
namespace RemoteDesk.Client.UI;
internal sealed class RemotePointerWindow:Window
{
    nint handle;
    internal RemotePointerWindow(Window owner)
    {
        Owner=owner;Title="RemoteDesk — Ponteiro remoto";WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;AllowsTransparency=true;Background=Brushes.Transparent;Topmost=true;ShowActivated=false;ShowInTaskbar=false;IsHitTestVisible=false;Width=32;Height=36;MinWidth=0;MinHeight=0;
        Content=new System.Windows.Shapes.Path{Data=Geometry.Parse("M3 2L3 25L9 20L14 30L19 27L14 18L24 18Z"),Fill=new SolidColorBrush(Color.FromRgb(29,111,242)),Stroke=Brushes.White,StrokeThickness=1.5};
        SourceInitialized+=(_,_)=>{handle=new WindowInteropHelper(this).Handle;var style=GetWindowLong(handle,-20);SetWindowLong(handle,-20,style|0x20|0x80|0x08000000);};
    }
    internal void Move(InputEvent input,System.Drawing.Rectangle bounds)
    {
        if(input.Kind is not ("move" or "button" or "wheel"))return;
        if(!IsVisible)Show();
        SetWindowPos(handle,new nint(-1),bounds.Left+(int)Math.Round(input.X*(bounds.Width-1))-3,bounds.Top+(int)Math.Round(input.Y*(bounds.Height-1))-2,0,0,0x0001|0x0010);
    }
    [DllImport("user32.dll")]static extern int GetWindowLong(nint window,int index);
    [DllImport("user32.dll")]static extern int SetWindowLong(nint window,int index,int value);
    [DllImport("user32.dll")]static extern bool SetWindowPos(nint window,nint after,int x,int y,int width,int height,uint flags);
}
