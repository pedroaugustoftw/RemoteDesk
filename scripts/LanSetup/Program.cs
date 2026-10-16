using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace RemoteDesk.LanSetup;
public record LanConfiguration(string Ip, string ServerUri, string RelayUri, string CertificateSha256, string CertificateFile);
public record Secrets(string CertificatePassword, string RelaySecret);
public record ProcessRecord(int Id, string Executable, string Assembly, long StartTicks);
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            if (args.Length < 2) throw new ArgumentException("Uso: setup <diretório> <IP LAN> | start <diretório> <dotnet.exe> <server.dll> <relay.dll> | check <diretório>");
            string root = Path.GetFullPath(args[1]);
            switch (args[0])
            {
                case "setup":
                    if (args.Length != 3 || !IPAddress.TryParse(args[2], out var ip) || ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) throw new ArgumentException("Informe um IPv4 válido.");
                    Setup(root, ip); return 0;
                case "start":
                    if (args.Length != 5) throw new ArgumentException("Caminhos de dotnet, server e relay são obrigatórios.");
                    await Start(root, args[2], args[3], args[4]); return 0;
                case "check":
                    try { await Check(root); return 0; }
                    catch { return 1; }
                default: throw new ArgumentException("Comando inválido.");
            }
        }
        catch (Exception ex) { Console.Error.WriteLine("RemoteDesk LAN: " + ex.Message); return 1; }
    }
    static void CheckDirectory(string root)
    {
        for (string? path = root; path != null; path = Path.GetDirectoryName(path))
            if (Directory.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("Diretório de configuração não pode conter links.");
    }
    static void Setup(string root, IPAddress ip)
    {
        CheckDirectory(root);
        Directory.CreateDirectory(root);
        var owner = WindowsIdentity.GetCurrent().User ?? throw new InvalidOperationException("Usuário Windows indisponível.");
        var security = new DirectorySecurity();
        security.SetOwner(owner); security.SetAccessRuleProtection(true, false);
        security.AddAccessRule(new FileSystemAccessRule(owner, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        try { new DirectoryInfo(root).SetAccessControl(security); }
        catch (UnauthorizedAccessException) { Console.WriteLine("ACL não pôde ser restringida neste ambiente; senha PFX e segredo continuam protegidos por DPAPI do usuário."); }
        if (File.Exists(Path.Combine(root, "secrets.dpapi"))) throw new IOException("Configuração existente. Preserve o certificado/fingerprint; utilize outro diretório para gerar uma identidade nova.");
        using var rsa = RSA.Create(3072);
        var request = new CertificateRequest("CN=RemoteDesk LAN", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var san = new SubjectAlternativeNameBuilder();
        san.AddIpAddress(ip); san.AddIpAddress(IPAddress.Loopback); san.AddDnsName("localhost"); san.AddDnsName(Environment.MachineName);
        request.CertificateExtensions.Add(san.Build());
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, true));
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(90));
        var secrets = new Secrets(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)), Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));
        string certificatePath = Path.Combine(root, "lan.pfx");
        using (var output = new FileStream(certificatePath, FileMode.CreateNew, FileAccess.Write, FileShare.None)) output.Write(certificate.Export(X509ContentType.Pfx, secrets.CertificatePassword));
        byte[] plaintext = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(secrets));
        try
        {
            byte[] encrypted = Dpapi.Protect(plaintext);
            using var output = new FileStream(Path.Combine(root, "secrets.dpapi"), FileMode.CreateNew, FileAccess.Write, FileShare.None);
            output.Write(encrypted);
        }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
        string fingerprint = certificate.GetCertHashString(HashAlgorithmName.SHA256);
        var configuration = new LanConfiguration(ip.ToString(), $"wss://{ip}:5081/ws", $"wss://{ip}:5091/ws", fingerprint, certificatePath);
        File.WriteAllText(Path.Combine(root, "lan.json"), JsonSerializer.Serialize(configuration, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Servidor: {configuration.ServerUri}\nRelay: {configuration.RelayUri}\nSHA256 TLS: {fingerprint}\nValidade: {certificate.NotAfter:yyyy-MM-dd}\nConfiguração: {root}");
    }
    static (LanConfiguration Configuration, Secrets Secrets) Load(string root)
    {
        CheckDirectory(root);
        var config = JsonSerializer.Deserialize<LanConfiguration>(File.ReadAllText(Path.Combine(root, "lan.json"))) ?? throw new IOException("Configuração ausente.");
        byte[] plaintext = Dpapi.Unprotect(File.ReadAllBytes(Path.Combine(root, "secrets.dpapi")));
        try { return (config, JsonSerializer.Deserialize<Secrets>(plaintext) ?? throw new IOException("Segredos ausentes.")); }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
    }
    static async Task Start(string root, string dotnet, string server, string relay)
    {
        var (config, secrets) = Load(root);
        dotnet = Path.GetFullPath(dotnet); server = Path.GetFullPath(server); relay = Path.GetFullPath(relay);
        if (!File.Exists(dotnet) || !File.Exists(server) || !File.Exists(relay)) throw new FileNotFoundException("Publique os componentes antes de iniciar.");
        string recordsPath = Path.Combine(root, "processes.json");
        if (File.Exists(recordsPath))
        {
            var records = JsonSerializer.Deserialize<ProcessRecord[]>(File.ReadAllText(recordsPath)) ?? [];
            foreach (var item in records)
            {
                try { using var existing = Process.GetProcessById(item.Id); if (existing.StartTime.ToUniversalTime().Ticks == item.StartTicks && !existing.HasExited) throw new IOException("Infraestrutura já em execução; utilize Stop-Lan.ps1."); }
                catch (ArgumentException) { }
            }
        }
        Directory.CreateDirectory(Path.Combine(root, "data"));
        Process StartChild(string assembly, string name, string urls)
        {
            var info = new ProcessStartInfo(dotnet) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = root, RedirectStandardOutput = true, RedirectStandardError = true };
            info.ArgumentList.Add(assembly);
            info.Environment["ASPNETCORE_URLS"] = urls;
            info.Environment["ASPNETCORE_Kestrel__Certificates__Default__Path"] = config.CertificateFile;
            info.Environment["ASPNETCORE_Kestrel__Certificates__Default__Password"] = secrets.CertificatePassword;
            info.Environment["RemoteDesk__RelaySecret"] = secrets.RelaySecret;
            info.Environment["RemoteDesk__RelayUrl"] = config.RelayUri;
            info.Environment["RemoteDesk__RelayApiUrl"] = "http://127.0.0.1:5092";
            info.Environment["RemoteDesk__RegistryPath"] = Path.Combine(root, "data", "devices.json");
            var child = Process.Start(info) ?? throw new IOException("Não foi possível iniciar " + name);
            _ = Pump(child.StandardOutput, Path.Combine(root, name + ".log"));
            _ = Pump(child.StandardError, Path.Combine(root, name + ".error.log"));
            return child;
        }
        using var relayProcess = StartChild(relay, "relay", "https://0.0.0.0:5091;http://127.0.0.1:5092");
        Process? serverProcess = null;
        try
        {
            serverProcess = StartChild(server, "server", "https://0.0.0.0:5081");
            var records = new[] { new ProcessRecord(serverProcess.Id, dotnet, server, serverProcess.StartTime.ToUniversalTime().Ticks), new ProcessRecord(relayProcess.Id, dotnet, relay, relayProcess.StartTime.ToUniversalTime().Ticks) };
            File.WriteAllText(recordsPath, JsonSerializer.Serialize(records, new JsonSerializerOptions { WriteIndented = true }));
            await Task.WhenAny(serverProcess.WaitForExitAsync(), relayProcess.WaitForExitAsync());
        }
        finally
        {
            if (serverProcess != null) { if (!serverProcess.HasExited) serverProcess.Kill(); serverProcess.Dispose(); }
            if (!relayProcess.HasExited) relayProcess.Kill();
        }
    }
    static async Task Pump(StreamReader source, string file)
    {
        while (await source.ReadLineAsync() is { } line) await File.AppendAllTextAsync(file, $"{DateTime.UtcNow:O} {line}\n");
    }
    static async Task Check(string root)
    {
        var (config, _) = Load(root);
        using var handler = new HttpClientHandler();
        handler.ServerCertificateCustomValidationCallback = (_, certificate, _, _) => certificate != null && certificate.NotAfter.ToUniversalTime() > DateTime.UtcNow && certificate.NotBefore.ToUniversalTime() <= DateTime.UtcNow && CryptographicOperations.FixedTimeEquals(certificate.GetCertHash(HashAlgorithmName.SHA256), Convert.FromHexString(config.CertificateSha256));
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
        using var response = await client.GetAsync("https://127.0.0.1:5081/health");
        response.EnsureSuccessStatusCode();
        using var relayResponse = await client.GetAsync("https://127.0.0.1:5091/health");
        relayResponse.EnsureSuccessStatusCode();
        Console.WriteLine("HTTPS server e relay health OK; SHA256 do certificado corresponde à configuração LAN.");
    }
}
