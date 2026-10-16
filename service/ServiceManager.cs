using System.ComponentModel;
using System.Runtime.InteropServices;
namespace RemoteDesk.Infrastructure;
public static class ServiceManager
{
    public const string Name="RemoteDeskServer";
    public static bool Exists(){var manager=OpenSCManager(null,null,1);if(manager==IntPtr.Zero)return false;try{var service=OpenService(manager,Name,4);if(service==IntPtr.Zero)return false;CloseServiceHandle(service);return true;}finally{CloseServiceHandle(manager);}}
    public static void Install(string executable)
    {
        var manager=OpenSCManager(null,null,3);Check(manager);try
        {
            var service=CreateService(manager,Name,"RemoteDesk Server",0xF01FF,0x10,2,1,"\""+Path.GetFullPath(executable)+"\"",null,IntPtr.Zero,null,"NT AUTHORITY\\LocalService",null);Check(service);
            try{var description=new DESCRIPTION{Value="RemoteDesk: servidor de sinalização e relay supervisionados. Não captura tela nem controla este computador."};ChangeServiceConfig2(service,1,ref description);}finally{CloseServiceHandle(service);}
        }finally{CloseServiceHandle(manager);}
    }
    public static void Start(){WithService(0x10,s=>{if(!StartService(s,0,IntPtr.Zero))throw new Win32Exception(Marshal.GetLastWin32Error());});}
    public static void Stop(){WithService(0x20|4,s=>{if(!ControlService(s,1,out _)&&Marshal.GetLastWin32Error()!=1062)throw new Win32Exception(Marshal.GetLastWin32Error());});}
    public static void Remove(){WithService(0x10000,s=>{if(!DeleteService(s))throw new Win32Exception(Marshal.GetLastWin32Error());});}
    private static void WithService(uint access,Action<IntPtr> action){var manager=OpenSCManager(null,null,1);Check(manager);try{var service=OpenService(manager,Name,access);Check(service);try{action(service);}finally{CloseServiceHandle(service);}}finally{CloseServiceHandle(manager);}}
    static void Check(IntPtr handle){if(handle==IntPtr.Zero)throw new Win32Exception(Marshal.GetLastWin32Error());}
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]struct DESCRIPTION{[MarshalAs(UnmanagedType.LPWStr)]public string Value;}
    [StructLayout(LayoutKind.Sequential)]struct STATUS{public uint Type,State,Accepted,Exit,Specific,Checkpoint,WaitHint;}
    [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern IntPtr OpenSCManager(string? machine,string? database,uint access);
    [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern IntPtr OpenService(IntPtr manager,string name,uint access);
    [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern IntPtr CreateService(IntPtr manager,string name,string display,uint access,uint type,uint start,uint error,string path,string? group,IntPtr tag,string? dependencies,string account,string? password);
    [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool ChangeServiceConfig2(IntPtr service,uint level,ref DESCRIPTION description);
    [DllImport("advapi32.dll",SetLastError=true)]static extern bool StartService(IntPtr service,uint count,IntPtr args);
    [DllImport("advapi32.dll",SetLastError=true)]static extern bool ControlService(IntPtr service,uint control,out STATUS status);
    [DllImport("advapi32.dll",SetLastError=true)]static extern bool DeleteService(IntPtr service);
    [DllImport("advapi32.dll")]static extern bool CloseServiceHandle(IntPtr handle);
}
