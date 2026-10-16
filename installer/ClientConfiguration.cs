using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RemoteDesk.Setup;
public record ConnectionConfiguration(string Server, string ServerCertificateSha256, string RelayCertificateSha256);
public static class ClientConfiguration
{
    public static ConnectionConfiguration Validate(string address, string serverPin, string relayPin)
    {
        address = address.Trim();
        if (address.Any(char.IsWhiteSpace) || address.Contains('\\') || !Uri.TryCreate(address, UriKind.Absolute, out var uri)
            || string.IsNullOrWhiteSpace(uri.Host) || uri.HostNameType == UriHostNameType.Unknown || uri.Port is < 1 or > 65535
            || uri.Scheme is not ("wss" or "ws") || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0
            || uri.AbsolutePath != "/ws")
            throw new ArgumentException("Informe o endereço completo: wss://IP-ou-host:5081/ws, sem usuário, senha ou parâmetros.");
        bool loopback = uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || (IPAddress.TryParse(uri.Host.Trim('[', ']'), out var ip) && IPAddress.IsLoopback(ip));
        if (uri.Scheme == "ws" && !loopback) throw new ArgumentException("Use wss:// para conectar a outro computador. ws:// é permitido somente em localhost ou IP de loopback.");
        return new(uri.AbsoluteUri, NormalizePin(serverPin), NormalizePin(relayPin));
    }
    public static string NormalizePin(string value)
    {
        string pin = string.Concat(value.Where(c => !char.IsWhiteSpace(c) && c != ':'));
        if ((pin.Length == 0 && !string.IsNullOrWhiteSpace(value)) || (pin.Length != 0 && (pin.Length != 64 || pin.Any(c => !Uri.IsHexDigit(c)))))
            throw new ArgumentException("O fingerprint SHA256 deve ter 64 caracteres hexadecimais. Deixe vazio para usar um certificado confiável pelo Windows.");
        return pin.ToUpperInvariant();
    }
    public static JsonObject Read(string path)
    {
        if (!File.Exists(path)) return new();
        CheckNoLinks(path);
        try { return JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? throw new InvalidDataException("Configuração existente não é um objeto JSON."); }
        catch (JsonException ex) { throw new InvalidDataException("A configuração existente não pôde ser lida. Corrija settings.json antes de atualizar para preservar seus dados.", ex); }
    }
    public static string Field(JsonObject settings, string field)
    {
        var match = settings.FirstOrDefault(x => x.Key.Equals(field, StringComparison.OrdinalIgnoreCase));
        return match.Value is JsonValue value && value.TryGetValue<string>(out var text) ? text : "";
    }
    public static string Merge(string existingJson, ConnectionConfiguration configuration)
    {
        var existing = JsonNode.Parse(existingJson) as JsonObject ?? throw new InvalidDataException("Configuração existente não é um objeto JSON.");
        Set(existing, "Server", configuration.Server);
        Set(existing, "ServerCertificateSha256", configuration.ServerCertificateSha256);
        Set(existing, "RelayCertificateSha256", configuration.RelayCertificateSha256);
        return existing.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }
    static void Set(JsonObject settings, string name, string value)
    {
        foreach (string key in settings.Select(x => x.Key).Where(x => x.Equals(name, StringComparison.OrdinalIgnoreCase)).ToArray()) settings.Remove(key);
        settings[name] = value;
    }
    public static void WriteAtomic(string root, ConnectionConfiguration configuration)
    {
        root = Path.GetFullPath(root);
        configuration = Validate(configuration.Server, configuration.ServerCertificateSha256, configuration.RelayCertificateSha256);
        string path = Path.Combine(Path.GetFullPath(root), "settings.json");
        CheckNoLinks(path);
        // Re-read after binary installation so unrelated fields changed since opening are retained.
        string updated = Merge(Read(path).ToJsonString(), configuration);
        Directory.CreateDirectory(root);
        string temporary = Path.Combine(root, ".settings-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                byte[] content = Encoding.UTF8.GetBytes(updated); output.Write(content); output.Flush(flushToDisk: true);
            }
            CheckNoLinks(path);
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    static void CheckNoLinks(string path)
    {
        for (string? current = path; current != null; current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Links não são aceitos no caminho da configuração.");
    }
    public static void Install(Stream payload, string root, ConnectionConfiguration? configuration)
    {
        // Reject unreadable existing settings before changing application binaries.
        if (configuration != null) { configuration = Validate(configuration.Server, configuration.ServerCertificateSha256, configuration.RelayCertificateSha256); _ = Read(Path.Combine(root, "settings.json")); }
        InstallTransaction.Install(payload, root);
        if (configuration != null) WriteAtomic(root, configuration);
    }
}
