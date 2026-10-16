using System.IO.Compression;
using RemoteDesk.Setup;

int passed = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception("FAIL: " + name); passed++; Console.WriteLine("PASS: " + name); }
MemoryStream Zip(params (string Name, string Data)[] files)
{
    var stream = new MemoryStream();
    using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
        foreach (var (name, data) in files) { using var writer = new StreamWriter(zip.CreateEntry(name).Open()); writer.Write(data); }
    stream.Position = 0;
    return stream;
}
void Reject(string path)
{
    using var bytes = Zip(("RemoteDesk.exe", "binary"), (path, "bad"));
    using var zip = new ZipArchive(bytes);
    try { PayloadPolicy.Validate(zip); throw new Exception("Unexpected accepted path: " + path); }
    catch (InvalidDataException) { Check(true, "reject " + path); }
}
foreach (var path in new[] { "../escape.txt", "..\\escape.txt", "/absolute.txt", "C:/escape.txt", "\\\\host\\share\\evil", "app/../../escape", "file:stream", "CON.txt", "NUL", "COM1.dll", "LPT9", "trailing. ", "app//file", "app/./file", "settings.json", "nested/identity.dpapi", "events.log" }) Reject(path);
using (var data = Zip(("RemoteDesk.exe", "binary"), ("REMOTEDESK.EXE", "duplicate")))
using (var zip = new ZipArchive(data))
{
    try { PayloadPolicy.Validate(zip); throw new Exception("Duplicate accepted"); }
    catch (InvalidDataException) { Check(true, "duplicate case aliases rejected"); }
}
using (var data = Zip(("RemoteDesk.exe", "binary"), ("dir", "file"), ("dir/child", "conflict")))
using (var zip = new ZipArchive(data))
{
    try { PayloadPolicy.Validate(zip); throw new Exception("File/directory alias accepted"); }
    catch (InvalidDataException) { Check(true, "file/directory collision rejected"); }
}
using (var data = Zip(("RemoteDesk.exe", "binary"), ("link", "target")))
{
    using (var zip = new ZipArchive(data, ZipArchiveMode.Update, true)) zip.GetEntry("link")!.ExternalAttributes = unchecked((int)0xA1FF0000);
    data.Position = 0;
    using var zipRead = new ZipArchive(data);
    try { PayloadPolicy.Validate(zipRead); throw new Exception("Symlink accepted"); }
    catch (InvalidDataException) { Check(true, "archive symlink rejected"); }
}
string sandbox = Path.Combine(Path.GetTempPath(), "RemoteDesk-installer-test-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(sandbox);
try
{
    string settings = Path.Combine(sandbox, "settings.json"), key = Path.Combine(sandbox, "identity.dpapi");
    File.WriteAllText(settings, "preserve-settings"); File.WriteAllText(key, "preserve-private-key");
    using (var data = Zip(("RemoteDesk.exe", "v1"), ("nested/runtime.dll", "runtime"))) InstallTransaction.Install(data, sandbox);
    Check(File.ReadAllText(Path.Combine(sandbox, "app", "RemoteDesk.exe")) == "v1", "sandbox payload extracted");
    using (var data = Zip(("RemoteDesk.exe", "v2"))) InstallTransaction.Install(data, sandbox);
    Check(File.ReadAllText(Path.Combine(sandbox, "app", "RemoteDesk.exe")) == "v2", "managed app updated");
    Check(!File.Exists(Path.Combine(sandbox, "app", "nested", "runtime.dll")), "obsolete application binaries removed on update");
    Check(File.ReadAllText(settings) == "preserve-settings" && File.ReadAllText(key) == "preserve-private-key", "profile and identity preserved");
    using (var bad = Zip(("RemoteDesk.exe", "bad"), ("../outside.txt", "evil")))
    {
        try { InstallTransaction.Install(bad, sandbox); throw new Exception("Traversal package installed"); }
        catch (InvalidDataException) { Check(File.ReadAllText(Path.Combine(sandbox, "app", "RemoteDesk.exe")) == "v2", "invalid update leaves current app intact"); }
    }
    Check(!Directory.EnumerateDirectories(sandbox).Any(x => Path.GetFileName(x).StartsWith(".install-") || Path.GetFileName(x).StartsWith(".previous-")), "temporary directories cleaned");
    string unmanaged = Path.Combine(sandbox, "unmanaged");
    Directory.CreateDirectory(Path.Combine(unmanaged, "app"));
    File.WriteAllText(Path.Combine(unmanaged, "app", "user.txt"), "personal");
    using (var data = Zip(("RemoteDesk.exe", "new")))
    {
        try { InstallTransaction.Install(data, unmanaged); throw new Exception("Unmanaged directory overwritten"); }
        catch (IOException) { Check(File.ReadAllText(Path.Combine(unmanaged, "app", "user.txt")) == "personal", "unmanaged app directory preserved"); }
    }
}
finally
{
    // sandbox is a fresh GUID-owned test directory, and no links are created by these tests.
    if (!Path.GetFullPath(sandbox).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)) throw new Exception("Unsafe cleanup path");
    Directory.Delete(sandbox, recursive: true);
}
foreach (string address in new[] { "", "wss://:5081/ws", "https://server:5081/ws", "ws://192.168.1.10:5081/ws", "ws://localhost.evil:5081/ws", "wss://user:password@server:5081/ws", "wss://server:5081/ws?token=x", "wss://server:5081/ws#fragment", "wss://server:0/ws", "wss://server:70000/ws", "wss://server:5081/other", "wss://ser ver:5081/ws", "wss://server\\ws" })
{
    try { ClientConfiguration.Validate(address, "", ""); throw new Exception("Invalid address accepted: " + address); }
    catch (ArgumentException) { Check(true, "invalid server address rejected: " + address); }
}
foreach (string address in new[] { "wss://192.168.1.10:5081/ws", "wss://server.example/ws", "ws://127.0.0.1:5081/ws", "ws://localhost:5081/ws", "ws://[::1]:5081/ws" })
    Check(ClientConfiguration.Validate(address, "", "").Server.StartsWith("ws"), "valid address accepted: " + address);
foreach (string pin in new[] { "::::", "abc", new string('G', 64), new string('A', 63), new string('A', 65) })
{
    try { ClientConfiguration.NormalizePin(pin); throw new Exception("Invalid fingerprint accepted"); }
    catch (ArgumentException) { Check(true, "invalid fingerprint rejected length=" + pin.Length); }
}
Check(ClientConfiguration.NormalizePin(string.Join(':', Enumerable.Repeat("ab", 32))) == string.Concat(Enumerable.Repeat("AB", 32)), "colon-separated lower-case SHA256 normalized");
string original = "{\"Name\":\"User alias\",\"DeviceId\":\"123\",\"Recent\":[{\"Id\":\"456\"}],\"Quality\":55,\"Unknown\":{\"nested\":[1,true,\"value\"]},\"Server\":\"ws://127.0.0.1:5080/ws\"}";
var configuration = ClientConfiguration.Validate("wss://192.168.1.10:5081/ws", new string('A', 64), new string('B', 64));
var merged = System.Text.Json.Nodes.JsonNode.Parse(ClientConfiguration.Merge(original, configuration))!.AsObject();
var initial = System.Text.Json.Nodes.JsonNode.Parse(original)!.AsObject();
Check(merged["Server"]!.GetValue<string>() == configuration.Server && merged["ServerCertificateSha256"]!.GetValue<string>() == new string('A', 64), "new endpoint and server pin merged");
Check(merged["RelayCertificateSha256"]!.GetValue<string>() == new string('B', 64), "relay pin independently stored");
Check(new[] { "Name", "DeviceId", "Recent", "Quality", "Unknown" }.All(x => System.Text.Json.Nodes.JsonNode.DeepEquals(initial[x], merged[x])), "alias identity recent quality and unknown nested fields preserved");
string configSandbox = Path.Combine(Path.GetTempPath(), "RemoteDesk-config-test-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(configSandbox);
try
{
    string settings = Path.Combine(configSandbox, "settings.json"), key = Path.Combine(configSandbox, "identity.dpapi");
    File.WriteAllText(settings, original); File.WriteAllText(key, "untouched-key");
    using (var bad = Zip(("RemoteDesk.exe", "bad"), ("../escape", "bad")))
    {
        try { ClientConfiguration.Install(bad, configSandbox, configuration); throw new Exception("Bad package accepted"); }
        catch (InvalidDataException) { Check(File.ReadAllText(settings) == original && !Directory.Exists(Path.Combine(configSandbox, "app")), "failed binaries never write connection settings"); }
    }
    using (var good = Zip(("RemoteDesk.exe", "v1"))) ClientConfiguration.Install(good, configSandbox, configuration);
    Check(ClientConfiguration.Field(ClientConfiguration.Read(settings), "Server") == configuration.Server, "successful binary install commits connection settings");
    Check(File.ReadAllText(key) == "untouched-key", "settings update never touches DPAPI identity");
    Check(!Directory.EnumerateFiles(configSandbox, ".settings-*.tmp").Any(), "atomic configuration write cleans temporary file");
    string unedited = File.ReadAllText(settings);
    using (var update = Zip(("RemoteDesk.exe", "v2"))) ClientConfiguration.Install(update, configSandbox, null);
    Check(File.ReadAllText(settings) == unedited, "unedited update preserves settings byte-for-byte");
    File.WriteAllText(settings, "malformed-json");
    using (var update = Zip(("RemoteDesk.exe", "v3")))
    {
        try { ClientConfiguration.Install(update, configSandbox, configuration); throw new Exception("Malformed settings overwritten"); }
        catch (InvalidDataException) { Check(File.ReadAllText(settings) == "malformed-json" && File.ReadAllText(Path.Combine(configSandbox, "app", "RemoteDesk.exe")) == "v2", "malformed existing settings block changes before installing binaries"); }
    }
}
finally
{
    if (!Path.GetFullPath(configSandbox).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)) throw new Exception("Unsafe cleanup path");
    Directory.Delete(configSandbox, recursive: true);
}
Console.WriteLine($"{passed} installer checks passed; no user installation, shortcut or process launch occurred.");
