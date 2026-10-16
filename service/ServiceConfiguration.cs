using System.Net;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using RemoteDesk.LanSetup;
namespace RemoteDesk.Infrastructure;
public record PublicConfiguration(string Ip,string ServerUri,string RelayUri,string CertificateSha256,string CertificateFile,int RelayApiPort=5092,string NetworkMode="local",string PublicServerUri="",string PublicRelayUri="",int TunnelPort=2750);
public record PrivateConfiguration(string CertificatePassword,string RelaySecret);
public static class ServiceConfiguration
{
    public static string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),"RemoteDeskServer");
    public static string AppRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"RemoteDesk Server");
    public static void CheckPath(string path)
    {
        for(string? current=Path.GetFullPath(path);current!=null;current=Path.GetDirectoryName(current))
            if(Directory.Exists(current)&&(File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0)throw new IOException("A pasta não pode conter links: "+current);
    }
    public static PublicConfiguration? Public(string root) => File.Exists(Path.Combine(root,"lan.json"))?JsonSerializer.Deserialize<PublicConfiguration>(File.ReadAllText(Path.Combine(root,"lan.json"))):null;
    public static (PublicConfiguration Public,PrivateConfiguration Private) Load(string root)
    {
        CheckPath(root);var config=Public(root)??throw new IOException("Configuração ausente.");
        byte[] bytes=Dpapi.Unprotect(File.ReadAllBytes(Path.Combine(root,"private","secrets.dpapi")));
        try{return(config,JsonSerializer.Deserialize<PrivateConfiguration>(bytes)??throw new IOException("Configuração inválida."));}finally{CryptographicOperations.ZeroMemory(bytes);}
    }
    public static PublicConfiguration Setup(string root,string ipText,bool applyAcl=true,bool migrate=true)
    {
        if(!IPAddress.TryParse(ipText,out var ip)||ip.AddressFamily!=System.Net.Sockets.AddressFamily.InterNetwork||IPAddress.IsLoopback(ip))throw new ArgumentException("Informe o IPv4 da rede local.");
        CheckPath(root);Directory.CreateDirectory(root);
        if(Public(root) is { } existing){if(existing.Ip!=ipText)throw new IOException("O serviço já possui outro IP. Preserve o certificado e ajuste a configuração conscientemente.");_ = Load(root);return existing;}
        string privateDir=Path.Combine(root,"private");CheckPath(privateDir);Directory.CreateDirectory(privateDir);
        if(applyAcl){SetAcl(root,false);SetAcl(privateDir,true);}
        PrivateConfiguration secrets;byte[] pfx;string fingerprint;
        string source=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"RemoteDesk","infrastructure");
        var original=migrate?Public(source):null;
        if(original!=null&&original.Ip==ipText)
        {
            CheckPath(source);byte[] plaintext=Dpapi.Unprotect(File.ReadAllBytes(Path.Combine(source,"secrets.dpapi")));
            try{secrets=JsonSerializer.Deserialize<PrivateConfiguration>(plaintext)??throw new IOException("Segredos LAN inválidos.");}finally{CryptographicOperations.ZeroMemory(plaintext);}
            pfx=File.ReadAllBytes(original.CertificateFile);using var cert=new X509Certificate2(pfx,secrets.CertificatePassword,X509KeyStorageFlags.EphemeralKeySet);
            fingerprint=cert.GetCertHashString(HashAlgorithmName.SHA256);if(!fingerprint.Equals(original.CertificateSha256,StringComparison.OrdinalIgnoreCase))throw new IOException("Certificado LAN não corresponde à impressão existente.");
        }
        else
        {
            using var rsa=RSA.Create(3072);var request=new CertificateRequest("CN=RemoteDesk Server",rsa,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1);
            var san=new SubjectAlternativeNameBuilder();san.AddIpAddress(ip);san.AddIpAddress(IPAddress.Loopback);san.AddDnsName("localhost");san.AddDnsName(Environment.MachineName);request.CertificateExtensions.Add(san.Build());
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false,false,0,true));request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature|X509KeyUsageFlags.KeyEncipherment,true));request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection{new("1.3.6.1.5.5.7.3.1")},true));
            using var cert=request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5),DateTimeOffset.UtcNow.AddDays(90));secrets=new(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));
            pfx=cert.Export(X509ContentType.Pfx,secrets.CertificatePassword);fingerprint=cert.GetCertHashString(HashAlgorithmName.SHA256);
        }
        string certificate=Path.Combine(privateDir,"lan.pfx");File.WriteAllBytes(certificate,pfx);
        byte[] clear=Encoding.UTF8.GetBytes(JsonSerializer.Serialize(secrets));try{File.WriteAllBytes(Path.Combine(privateDir,"secrets.dpapi"),Dpapi.ProtectMachine(clear));}finally{CryptographicOperations.ZeroMemory(clear);}
        var result=new PublicConfiguration(ipText,$"wss://{ipText}:5081/ws",$"wss://{ipText}:5091/ws",fingerprint,certificate);
        Directory.CreateDirectory(Path.Combine(root,"data"));
        string registry=Path.Combine(source,"data","devices.json");if(original!=null&&File.Exists(registry))File.Copy(registry,Path.Combine(root,"data","devices.json"),false);
        File.WriteAllText(Path.Combine(root,"lan.json"),JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));return result;
    }
    public static PublicConfiguration ConfigureNetwork(string root,string mode,string server,string relay,int port)
    {
        var c=Public(root)??throw new IOException("Instale os componentes antes de salvar a rede.");
        if(mode is not ("local" or "vps"))throw new ArgumentException("Modo de rede inválido.");
        if(port<1024||port>65535||new[]{new Uri(c.ServerUri).Port,new Uri(c.RelayUri).Port,c.RelayApiPort}.Contains(port))throw new ArgumentException("Porta do túnel inválida ou em uso pelos componentes.");
        if(mode=="vps")foreach(var value in new[]{server,relay})
            if(!Uri.TryCreate(value,UriKind.Absolute,out var uri)||uri.Scheme!="wss"||string.IsNullOrEmpty(uri.Host)||uri.UserInfo!=""||uri.Fragment!=""||uri.Query!="")throw new ArgumentException("Informe endereços públicos wss:// válidos, sem senha, consulta ou fragmento.");
        var updated=c with {NetworkMode=mode,PublicServerUri=server.Trim(),PublicRelayUri=relay.Trim(),TunnelPort=port};
        string file=Path.Combine(root,"lan.json");File.WriteAllText(file+".tmp",JsonSerializer.Serialize(updated,new JsonSerializerOptions{WriteIndented=true}));File.Move(file+".tmp",file,true);return updated;
    }
    public static void SetAcl(string root,bool secret)
    {
        var acl=new DirectorySecurity();acl.SetAccessRuleProtection(true,false);acl.SetOwner(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid,null));
        foreach(var sid in new[]{WellKnownSidType.BuiltinAdministratorsSid,WellKnownSidType.LocalSystemSid,WellKnownSidType.LocalServiceSid})acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(sid,null),FileSystemRights.FullControl,InheritanceFlags.ContainerInherit|InheritanceFlags.ObjectInherit,PropagationFlags.None,AccessControlType.Allow));
        if(!secret)acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid,null),FileSystemRights.ReadAndExecute,InheritanceFlags.ContainerInherit|InheritanceFlags.ObjectInherit,PropagationFlags.None,AccessControlType.Allow));
        new DirectoryInfo(root).SetAccessControl(acl);
    }
}
