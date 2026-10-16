using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using RemoteDesk.Shared;

namespace RemoteDesk.Client.RemoteControl;

/// <summary>Session-gated by caller. Windows UIPI and secure desktop restrictions are preserved.</summary>
public sealed class InputController
{
    private readonly HashSet<int> keys = new();
    private readonly HashSet<int> buttons = new();
    private readonly object gate = new();

    public void Apply(InputEvent input, Rectangle bounds)
    {
        lock (gate)
        {
            switch (input.Kind)
            {
                case "move":
                    var position = Normalize(input.X, input.Y, bounds, SystemInformation.VirtualScreen);
                    SendMouse(position.X, position.Y, 0, 0x0001 | 0x8000 | 0x4000);
                    break;
                case "button" when input.Button is >= 0 and <= 2:
                    if(input.Down) { var clickPosition = Normalize(input.X, input.Y, bounds, SystemInformation.VirtualScreen); SendMouse(clickPosition.X, clickPosition.Y, 0, 0x0001 | 0x8000 | 0x4000); }
                    if (input.Down ? buttons.Add(input.Button) : buttons.Remove(input.Button))
                        SendMouse(0, 0, 0, ButtonFlag(input.Button, input.Down));
                    break;
                case "wheel" when input.Delta is >= -1200 and <= 1200:
                    var wheelPosition = Normalize(input.X, input.Y, bounds, SystemInformation.VirtualScreen);
                    SendMouse(wheelPosition.X, wheelPosition.Y, 0, 0x0001 | 0x8000 | 0x4000);
                    SendMouse(0, 0, unchecked((uint)input.Delta), 0x0800);
                    break;
                case "key" when input.Key is >= 1 and <= 254:
                    if (input.Down ? keys.Add(input.Key) : keys.Remove(input.Key)) SendKey(input.Key, input.Down);
                    break;
                default: throw new ArgumentException("Evento de entrada inválido.", nameof(input));
            }
        }
    }

    public void ReleaseAll()
    {
        lock (gate)
        {
            // Attempt every release even if one is rejected by Windows.
            foreach (var key in keys.ToArray())
                try { SendKey(key, false); keys.Remove(key); } catch (InvalidOperationException) { }
            foreach (var button in buttons.ToArray())
                try { SendMouse(0, 0, 0, ButtonFlag(button, false)); buttons.Remove(button); } catch (InvalidOperationException) { }
        }
    }

    // Pure function; useful for coordinate tests without injecting input on this computer.
    public static Point Normalize(double x, double y, Rectangle monitor, Rectangle desktop)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y) || x < 0 || x > 1 || y < 0 || y > 1 ||
            monitor.Width < 1 || monitor.Height < 1 || desktop.Width < 2 || desktop.Height < 2)
            throw new ArgumentOutOfRangeException(nameof(x));
        var px = monitor.Left + x * (monitor.Width - 1);
        var py = monitor.Top + y * (monitor.Height - 1);
        return new Point((int)Math.Round(Math.Clamp((px - desktop.Left) / (desktop.Width - 1), 0, 1) * 65535),
            (int)Math.Round(Math.Clamp((py - desktop.Top) / (desktop.Height - 1), 0, 1) * 65535));
    }

    private static uint ButtonFlag(int button, bool down) => button switch
    {
        0 => down ? 0x0002u : 0x0004u,
        1 => down ? 0x0008u : 0x0010u,
        2 => down ? 0x0020u : 0x0040u,
        _ => throw new ArgumentOutOfRangeException(nameof(button))
    };
    private static void SendMouse(int x, int y, uint data, uint flags) => Submit(new INPUT
    {
        type = 0, union = new InputUnion { mouse = new MOUSEINPUT { dx = x, dy = y, mouseData = data, dwFlags = flags } }
    });
    private static void SendKey(int key, bool down)
    {
        var extended = key is 0x21 or 0x22 or 0x23 or 0x24 or 0x25 or 0x26 or 0x27 or 0x28 or
            0x2D or 0x2E or 0x5B or 0x5C or 0x5D or 0x6F or 0x90 or 0xA3 or 0xA5;
        Submit(new INPUT { type = 1, union = new InputUnion { keyboard = new KEYBDINPUT
            { wVk = (ushort)key, dwFlags = (down ? 0u : 2u) | (extended ? 1u : 0u) } } });
    }
    private static void Submit(INPUT input)
    {
        if (SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>()) != 1)
            throw new InvalidOperationException("Windows recusou entrada remota (UIPI, área segura ou sessão indisponível).");
    }
    [StructLayout(LayoutKind.Sequential)] private struct INPUT { public uint type; public InputUnion union; }
    [StructLayout(LayoutKind.Explicit)] private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mouse;
        [FieldOffset(0)] public KEYBDINPUT keyboard;
    }
    [StructLayout(LayoutKind.Sequential)] private struct MOUSEINPUT
    { public int dx, dy; public uint mouseData, dwFlags, time; public UIntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] private struct KEYBDINPUT
    { public ushort wVk, wScan; public uint dwFlags, time; public UIntPtr dwExtraInfo; }
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, INPUT[] inputs, int size);
}
