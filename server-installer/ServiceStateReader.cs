using System.Runtime.InteropServices;
using RemoteDesk.Infrastructure;
namespace RemoteDesk.ServerSetup;
internal static class ServiceStateReader
{
 internal static (int Code,string Label) Read()
 {
  var manager=OpenSCManager(null,null,1);if(manager==IntPtr.Zero)return(-1,"Consulta indisponível");
  try
  {
   var service=OpenService(manager,ServiceManager.Name,4);
   if(service==IntPtr.Zero)return Marshal.GetLastWin32Error()==1060?(0,"Não instalado"):(-1,"Consulta indisponível");
   try
   {
    if(!QueryServiceStatus(service,out var status))return(-1,"Consulta indisponível");
    return((int)status.State,status.State switch{1=>"Parado",2=>"Iniciando…",3=>"Parando…",4=>"Em execução",5=>"Retomando…",6=>"Pausando…",7=>"Pausado",_=>"Estado desconhecido"});
   }finally{CloseServiceHandle(service);}
  }finally{CloseServiceHandle(manager);}
 }
 [StructLayout(LayoutKind.Sequential)]struct Status{public uint Type,State,Accepted,Exit,Specific,Checkpoint,WaitHint;}
 [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern IntPtr OpenSCManager(string? machine,string? database,uint access);
 [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern IntPtr OpenService(IntPtr manager,string name,uint access);
 [DllImport("advapi32.dll",SetLastError=true)]static extern bool QueryServiceStatus(IntPtr service,out Status status);
 [DllImport("advapi32.dll")]static extern bool CloseServiceHandle(IntPtr handle);
}
