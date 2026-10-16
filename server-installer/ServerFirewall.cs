using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using RemoteDesk.Infrastructure;
namespace RemoteDesk.ServerSetup;
internal static class ServerFirewall
{
    internal static object ExistingOrNew(Func<object> lookup,Func<object> create)
    {
        try{return lookup();}
        // COM HRESULT_FROM_WIN32(ERROR_FILE_NOT_FOUND) is mapped to
        // FileNotFoundException by .NET, rather than always COMException.
        catch(Exception ex)when(ex.HResult==unchecked((int)0x80070002)){return create();}
    }
    internal static void ApplyRule(Func<object> lookup,Func<object> create,Action<object> configure,Action<object> add)
    {
        bool created=false;object rule=ExistingOrNew(lookup,()=>{created=true;return create();});
        try{configure(rule);if(created)add(rule);}
        finally{if(Marshal.IsComObject(rule))Marshal.FinalReleaseComObject(rule);}
    }
    internal static void Configure()
    {
        var config=ServiceConfiguration.Public(ServiceConfiguration.Root)??throw new IOException("Instale a configuração do servidor primeiro.");
        if(!IPAddress.TryParse(config.Ip,out var ip)||IPAddress.IsLoopback(ip))throw new IOException("IP LAN inválido.");
        dynamic policy=Activator.CreateInstance(Type.GetTypeFromProgID("HNetCfg.FwPolicy2")!)!;
        try
        {
            foreach(var item in new[]{("Server","server/RemoteDesk.Server.exe",config.ServerUri),("Relay","relay/RemoteDesk.Relay.exe",config.RelayUri)})
            {
                var uri=new Uri(item.Item3);string program=Path.GetFullPath(Path.Combine(ServiceConfiguration.AppRoot,item.Item2));
                if(uri.Scheme!="wss"||uri.Host!=config.Ip||!File.Exists(program))throw new IOException("Componente ou endereço LAN inválido.");
                ApplyRule(()=>policy.Rules.Item("RemoteDesk-LAN-"+item.Item1),()=>Activator.CreateInstance(Type.GetTypeFromProgID("HNetCfg.FWRule")!)!,value=>{
                    dynamic rule=value;
                    rule.Name="RemoteDesk-LAN-"+item.Item1;rule.Description="RemoteDesk: somente clientes da sub-rede local.";
                    rule.ApplicationName=program;rule.Protocol=6;rule.LocalPorts=uri.Port.ToString();rule.LocalAddresses=config.Ip;
                    rule.RemoteAddresses="LocalSubnet";rule.Direction=1;rule.Profiles=int.MaxValue;rule.Action=1;rule.Enabled=true;
                },value=>policy.Rules.Add(value));
            }
        }
        finally{Marshal.FinalReleaseComObject(policy);}
    }
}
