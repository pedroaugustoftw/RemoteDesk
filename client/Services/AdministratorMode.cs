using System;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
namespace RemoteDesk.Client.Services;
internal static class AdministratorMode
{
    internal static bool IsElevated=>new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
    internal static ProcessStartInfo RestartInfo()
    {
        var executable=Environment.ProcessPath??throw new IOException("Executável não identificado.");
        var start=new ProcessStartInfo(executable){UseShellExecute=true,Verb="runas"};
        if(Path.GetFileNameWithoutExtension(executable).Equals("dotnet",StringComparison.OrdinalIgnoreCase))start.ArgumentList.Add(Path.GetFullPath(Environment.GetCommandLineArgs()[0]));
        start.ArgumentList.Add("--wait-for-parent");start.ArgumentList.Add(Environment.ProcessId.ToString());return start;
    }
}
