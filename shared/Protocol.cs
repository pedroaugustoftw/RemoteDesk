using System.Text.Json;
namespace RemoteDesk.Shared;
public record Permissions(bool View = false, bool Control = false, bool Files = false)
{
    public bool IsSubsetOf(Permissions other) => (!View || other.View) && (!Control || other.Control) && (!Files || other.Files) && (!Control || View);
}
public record InputEvent(string Kind, double X = 0, double Y = 0, int Button = 0, int Delta = 0, int Key = 0, bool Down = false);
public record DeviceSummary(string Id, string Name);
public record DevicePresence(string Id,string Name,bool Online);
public record FileEntry(string Name,string Path,bool Directory,long Size=0,DateTime? Modified=null);
public record FileMessage(string Kind, string TransferId, string? Name = null, long Size = 0, string? Hash = null, string? Chunk = null, long Offset = 0,string? Path=null,FileEntry[]? Entries=null,string? Error=null,bool More=false,bool Replace=false);
public record Envelope
{
    public string Type { get; init; } = "";
    public string? RequestId { get; init; }
    public string? SessionId { get; init; }
    public string? TargetId { get; init; }
    public string? DeviceId { get; init; }
    public string? PublicKey { get; init; }
    public string? Nonce { get; init; }
    public string? Signature { get; init; }
    public string? Name { get; init; }
    public Permissions? Permissions { get; init; }
    public string? Payload { get; init; }
    public InputEvent? Data { get; init; }
    public FileMessage? File { get; init; }
    public string? Error { get; init; }
    public string? RelayUrl { get; init; }
    public string? RelayTicket { get; init; }
    public bool? IsHost { get; init; }
    public bool PasswordAccess { get; init; }
    public int? QualityLevel { get; init; }
}
public record RelayRegistration(string SessionId, Permissions Permissions);
public record RelayTickets(string HostTicket, string ViewerTicket);
public static class MediaPolicy
{
    public static bool Valid(Envelope m, bool isHost, Permissions permissions) => m.Type switch
    {
        "frame" => isHost && permissions.View && m.Payload is {Length: >0 and <=1900000},
        "quality" => !isHost && permissions.View && m.QualityLevel is >=0 and <=100,
        "probe" => !isHost && permissions.View && m.Payload is {Length:32}&&Guid.TryParseExact(m.Payload,"N",out _),
        "probe-reply" => isHost && permissions.View && m.Payload is {Length:32}&&Guid.TryParseExact(m.Payload,"N",out _),
        "input" => !isHost && permissions.Control && m.Data is {} d && double.IsFinite(d.X) && double.IsFinite(d.Y) && d.X>=0 && d.X<=1 && d.Y>=0 && d.Y<=1 && d.Kind is "move" or "button" or "wheel" or "key" && d.Key>=0 && d.Key<=255 && d.Button>=0 && d.Button<=3 && Math.Abs((long)d.Delta)<=12000,
        "file" => permissions.Files && ValidFile(m.File) && (m.File!.Kind is "browse" or "pull" or "mkdir" ? !isHost : m.File.Kind=="listing" ? isHost : true),
        _ => false
    };
    public static bool ValidFile(FileMessage? f)
    {
        if(f is null || f.TransferId is not {Length: >0 and <=64} || f.Name?.Length is >255 || f.Size<0 || f.Size>2L*1024*1024*1024 || f.Offset<0 || f.Offset>2L*1024*1024*1024) return false;
        if(f.Path?.Length>2048||f.Path?.Contains('\0')==true||f.Error?.Length>512)return false;
        if(f.Kind is "browse" or "listing" or "pull" or "mkdir")
        {
            if(!Guid.TryParseExact(f.TransferId,"N",out _)||f.Path is null)return false;
            if(f.Kind=="listing")return f.Entries is {Length:<=100}&&f.Entries.All(e=>e is not null&&e.Name is {Length:>0 and <=255}&&e.Name is not ("." or "..")&&!e.Name.Any(c=>char.IsControl(c)||"\\/:*?\"<>|".Contains(c))&&e.Path is {Length:>0 and <=2048}&&e.Size>=0);
            return f.Entries is null&&f.Chunk is null&&(f.Kind=="browse"||!string.IsNullOrWhiteSpace(f.Path));
        }
        if(f.Entries is not null)return false;
        if(f.Kind=="chunk")
        {
            if(f.Chunk is null || f.Chunk.Length>87384) return false;
            try { return Convert.FromBase64String(f.Chunk).Length is >0 and <=65536; } catch(FormatException) { return false; }
        }
        return f.Kind is "offer" or "accept" or "reject" or "complete" or "cancel" or "verified" or "ack";
    }
}
public static class Wire
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public static byte[] Encode(Envelope message) => JsonSerializer.SerializeToUtf8Bytes(message, Json);
    public static Envelope? Decode(ReadOnlySpan<byte> data) => JsonSerializer.Deserialize<Envelope>(data, Json);
    public const int MaxMessageBytes = 2 * 1024 * 1024;
}
