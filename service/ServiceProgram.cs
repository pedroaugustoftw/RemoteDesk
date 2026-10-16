using System.Diagnostics;
using System.Net.Http;
using System.Runtime.InteropServices;
using RemoteDesk.Infrastructure;
internal static class Program
{
    static readonly CancellationTokenSource stop=new();static IntPtr statusHandle;
    static readonly ServiceMainDelegate entry=MainService;static readonly HandlerDelegate handler=Control;
    static readonly object statusLock=new();
    static int Main(string[] args)
    {
        if(args.Contains("--console")){string root=args.Length>1?args[1]:ServiceConfiguration.Root;_=WatchConsoleStop(root);Run(root).GetAwaiter().GetResult();return 0;}
        var table=new[]{new SERVICE_TABLE{Name=ServiceManager.Name,Entry=Marshal.GetFunctionPointerForDelegate(entry)},new SERVICE_TABLE{ Name=null,Entry=IntPtr.Zero}};
        bool ok=StartServiceCtrlDispatcher(table);GC.KeepAlive(entry);GC.KeepAlive(handler);return ok?0:Marshal.GetLastWin32Error();
    }
    static void MainService(uint count,IntPtr args)
    {
        statusHandle=RegisterServiceCtrlHandlerEx(ServiceManager.Name,handler,IntPtr.Zero);if(statusHandle==IntPtr.Zero)return;
        Status(2,0,1,30000);
        try{Run(ServiceConfiguration.Root).GetAwaiter().GetResult();Status(1,0);}
        catch(Exception e){Log("service-error "+e.Message);Status(1,0,exit:1);}
    }
    static uint Control(uint control,uint eventType,IntPtr eventData,IntPtr context){if(control is 1 or 5){Status(3,0,1,15000);stop.Cancel();}return 0;}
    static void Status(uint state,uint accepted,uint checkpoint=0,uint wait=0,uint exit=0)
    {
        lock(statusLock){var s=new SERVICE_STATUS{Type=0x10,State=state,Accepted=accepted,Exit=exit,Checkpoint=checkpoint,WaitHint=wait};SetServiceStatus(statusHandle,ref s);}
    }
    static void Log(string value){try{Directory.CreateDirectory(ServiceConfiguration.Root);File.AppendAllText(Path.Combine(ServiceConfiguration.Root,"service.log"),DateTime.UtcNow.ToString("O")+" "+value+"\n");}catch{}}
    static async Task WatchConsoleStop(string root){while(!stop.IsCancellationRequested){if(File.Exists(Path.Combine(root,"console.stop"))){stop.Cancel();return;}await Task.Delay(250);}}
    static async Task Run(string root)
    {
        var (config,secrets)=ServiceConfiguration.Load(root);string baseDir=AppContext.BaseDirectory;
        Process Launch(string relative,string name,string urls)
        {
            string exe=Path.GetFullPath(Path.Combine(baseDir,"..",relative));if(!File.Exists(exe))throw new IOException("Componente ausente: "+relative);
            var info=new ProcessStartInfo(exe){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=root,RedirectStandardOutput=true,RedirectStandardError=true};
            info.Environment["ASPNETCORE_URLS"]=urls;info.Environment["ASPNETCORE_Kestrel__Certificates__Default__Path"]=config.CertificateFile;info.Environment["ASPNETCORE_Kestrel__Certificates__Default__Password"]=secrets.CertificatePassword;
            info.Environment["RemoteDesk__RelaySecret"]=secrets.RelaySecret;info.Environment["RemoteDesk__RelayUrl"]=config.NetworkMode=="vps"?config.PublicRelayUri:config.RelayUri;info.Environment["RemoteDesk__RelayApiUrl"]=$"http://127.0.0.1:{config.RelayApiPort}";info.Environment["RemoteDesk__RegistryPath"]=Path.Combine(root,"data","devices.json");
            if(name=="server"&&config.NetworkMode=="vps")
            {
                info.Environment["RemoteDesk__GatewayRelayUrl"]=$"wss://127.0.0.1:{new Uri(config.RelayUri).Port}/ws";
                info.Environment["RemoteDesk__GatewayRelaySha256"]=config.CertificateSha256;
            }
            // Single-file runtime extraction stays inside the service's private directory.
            info.Environment["DOTNET_BUNDLE_EXTRACT_BASE_DIR"]=Path.Combine(root,"private","runtime");
            var process=Process.Start(info)??throw new IOException("Falha iniciando "+name);
            _=Pump(process.StandardOutput,Path.Combine(root,name+".log"));_=Pump(process.StandardError,Path.Combine(root,name+".error.log"));return process;
        }
        int serverPort=new Uri(config.ServerUri).Port,relayPort=new Uri(config.RelayUri).Port;
        using var relay=Launch("relay/RemoteDesk.Relay.exe","relay",$"https://0.0.0.0:{relayPort};http://127.0.0.1:{config.RelayApiPort}");Process? server=null;
        try
        {
            server=Launch("server/RemoteDesk.Server.exe","server",$"https://0.0.0.0:{serverPort}"+(config.NetworkMode=="vps"?$";http://localhost:{config.TunnelPort}":""));
            using var httpHandler=new HttpClientHandler();httpHandler.ServerCertificateCustomValidationCallback=(_,cert,_,_)=>cert?.GetCertHashString(System.Security.Cryptography.HashAlgorithmName.SHA256)==config.CertificateSha256;
            using var client=new HttpClient(httpHandler){Timeout=TimeSpan.FromSeconds(2)};bool ready=false;
            for(int attempt=0;attempt<25&&!stop.IsCancellationRequested;attempt++)
            {
                if(statusHandle!=IntPtr.Zero)Status(2,0,(uint)attempt+1,30000);
                if(server.HasExited||relay.HasExited)throw new IOException("Componente encerrou; confira os logs. As portas 5081/5091 podem estar ocupadas por Start-Lan.");
                try{using var one=await client.GetAsync($"https://127.0.0.1:{serverPort}/health",stop.Token);using var two=await client.GetAsync($"https://127.0.0.1:{relayPort}/health",stop.Token);if(one.IsSuccessStatusCode&&two.IsSuccessStatusCode){ready=true;break;}}catch(HttpRequestException){}catch(TaskCanceledException)when(!stop.IsCancellationRequested){}
                await Task.Delay(500,stop.Token);
            }
            if(!ready)throw new IOException("Servidor não respondeu no prazo de inicialização.");
            if(statusHandle!=IntPtr.Zero)Status(4,1|4);Log("running "+config.ServerUri);
            await Task.WhenAny(server.WaitForExitAsync(),relay.WaitForExitAsync(),Task.Delay(Timeout.Infinite,stop.Token));
            if(!stop.IsCancellationRequested)throw new IOException("Um componente encerrou inesperadamente.");
        }
        catch(OperationCanceledException)when(stop.IsCancellationRequested){}
        finally{if(server!=null){if(!server.HasExited)server.Kill(entireProcessTree:true);server.Dispose();}if(!relay.HasExited)relay.Kill(entireProcessTree:true);}
    }
    static async Task Pump(StreamReader source,string path){try{while(await source.ReadLineAsync() is {} line)await File.AppendAllTextAsync(path,DateTime.UtcNow.ToString("O")+" "+line+"\n");}catch{} }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]delegate void ServiceMainDelegate(uint count,IntPtr args);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]delegate uint HandlerDelegate(uint control,uint type,IntPtr data,IntPtr context);
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]struct SERVICE_TABLE{[MarshalAs(UnmanagedType.LPWStr)]public string? Name;public IntPtr Entry;}
    [StructLayout(LayoutKind.Sequential)]struct SERVICE_STATUS{public uint Type,State,Accepted,Exit,Specific,Checkpoint,WaitHint;}
    [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool StartServiceCtrlDispatcher([In] SERVICE_TABLE[] table);
    [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern IntPtr RegisterServiceCtrlHandlerEx(string name,HandlerDelegate callback,IntPtr context);
    [DllImport("advapi32.dll",SetLastError=true)]static extern bool SetServiceStatus(IntPtr handle,ref SERVICE_STATUS status);
}
