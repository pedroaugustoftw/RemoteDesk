using Microsoft.Win32;
using System.IO;
namespace RemoteDesk.Client.Services;
public static class StartupRegistration
{
    const string RunKey=@"Software\Microsoft\Windows\CurrentVersion\Run";
    public static bool Enabled { get { using var key=Registry.CurrentUser.OpenSubKey(RunKey);return key?.GetValue("RemoteDesk") is string; } }
    public static void Set(bool enabled,string executable)
    {
        using var key=Registry.CurrentUser.CreateSubKey(RunKey);
        if(enabled){executable=Path.GetFullPath(executable);if(!File.Exists(executable)||executable.Contains('"'))throw new IOException("Executável RemoteDesk inválido.");key.SetValue("RemoteDesk","\""+executable+"\" --minimized",RegistryValueKind.String);}
        else key.DeleteValue("RemoteDesk",false);
    }
}
