using System.IO;
using System.Text.Json;
using System.Security.Cryptography;
namespace RemoteDesk.Client.Services;
public record RecentDevice(string Id, string Name, DateTime Seen);
public record SavedDevice(string Id,string Name,bool PasswordAccess=false,string PasswordDpapi="",string KeySha256="");
public sealed class Profile
{
    public string NetworkMode { get; set; } = "local";
    public string LocalServer { get; set; } = "";
    public string PublicServer { get; set; } = "";
    public string LocalServerPin { get; set; } = "";
    public string LocalRelayPin { get; set; } = "";
    public string Server { get; set; } = "ws://127.0.0.1:5080/ws";
    public string Name { get; set; } = Environment.MachineName;
    public string DeviceId { get; set; } = "";
    public string ServerCertificateSha256 { get; set; } = "";
    public string RelayCertificateSha256 { get; set; } = "";
    public int Quality { get; set; } = 65;
    public int MaxWidth { get; set; } = 1280;
    public int Fps { get; set; } = 10;
    public int Monitor { get; set; }
    public bool AutoReconnect { get; set; } = true;
    public List<RecentDevice> Recent { get; set; } = [];
    public List<RecentDevice> Favorites { get; set; } = [];
    public List<SavedDevice> Devices { get; set; } = [];
    public bool NotifySound { get; set; }
    [System.Text.Json.Serialization.JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtraSettings { get; set; }
    public static string DirectoryPath { get; } = Path.GetFullPath(Environment.GetEnvironmentVariable("REMOTEDESK_PROFILE") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RemoteDesk"));
    public static Profile Load()
    {
        Directory.CreateDirectory(DirectoryPath);
        string path = Path.Combine(DirectoryPath, "settings.json");
        if (!File.Exists(path)) return new();
        return JsonSerializer.Deserialize<Profile>(File.ReadAllText(path)) ?? new();
    }
    public void Save()
    {
        string path = Path.Combine(DirectoryPath, "settings.json");
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(path + ".tmp", path, true);
    }
    public static RSA LoadKey()
    {
        Directory.CreateDirectory(DirectoryPath);
        string path = Path.Combine(DirectoryPath, "identity.dpapi");
        var rsa = RSA.Create(3072);
        if (File.Exists(path)) rsa.ImportPkcs8PrivateKey(ProtectedData.Unprotect(File.ReadAllBytes(path), null, DataProtectionScope.CurrentUser), out _);
        else File.WriteAllBytes(path, ProtectedData.Protect(rsa.ExportPkcs8PrivateKey(), null, DataProtectionScope.CurrentUser));
        return rsa;
    }
    public static void Log(string message) => File.AppendAllText(Path.Combine(DirectoryPath, "events.log"), $"{DateTime.UtcNow:O} {message}\n");
}
