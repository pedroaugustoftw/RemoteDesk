using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text.Json;
using RemoteDesk.Shared;
namespace RemoteDesk.Server;
public sealed class Broker : BackgroundService
{
    readonly ConcurrentDictionary<string, Peer> peers = new();
    readonly ConcurrentDictionary<string, Session> sessions = new();
    readonly object registryLock = new();
    readonly object sessionLock = new();
    readonly Dictionary<string, string> registry;
    readonly string registryPath;
    readonly ILogger<Broker> log;
    readonly string? relayUrl;
    readonly HttpClient? relayApi;
    public Broker(IConfiguration config, ILogger<Broker> logger)
    {
        log = logger;
        relayUrl=config["RemoteDesk:RelayUrl"];
        if(!string.IsNullOrWhiteSpace(relayUrl))
        {
            var media=new Uri(relayUrl);
            if(media.Scheme!="wss" && !(media.Scheme=="ws" && media.IsLoopback)) throw new InvalidOperationException("RelayUrl requires WSS except loopback.");
            var secret=config["RemoteDesk:RelaySecret"];
            if(secret is null || secret.Length<32) throw new InvalidOperationException("RelaySecret must have at least 32 characters.");
            relayApi=new HttpClient {BaseAddress=new Uri((config["RemoteDesk:RelayApiUrl"]??throw new InvalidOperationException("RelayApiUrl required")).TrimEnd('/')+"/"),Timeout=TimeSpan.FromSeconds(5)};
            relayApi.DefaultRequestHeaders.Authorization=new("Bearer",secret);
        }
        registryPath = Path.GetFullPath(config["RemoteDesk:RegistryPath"] ?? "data/devices.json");
        Directory.CreateDirectory(Path.GetDirectoryName(registryPath)!);
        registry = System.IO.File.Exists(registryPath) ? JsonSerializer.Deserialize<Dictionary<string,string>>(System.IO.File.ReadAllText(registryPath))! : new();
    }
    string Identity(string key)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Convert.FromBase64String(key)));
        lock(registryLock)
        {
            if (registry.TryGetValue(hash, out var id)) return id;
            do { id = RandomNumberGenerator.GetInt32(100000000,999999999).ToString(); } while(registry.ContainsValue(id));
            registry.Add(hash,id);
            System.IO.File.WriteAllText(registryPath + ".tmp",JsonSerializer.Serialize(registry));
            System.IO.File.Move(registryPath + ".tmp",registryPath,true);
            return id;
        }
    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer=new PeriodicTimer(TimeSpan.FromSeconds(1));
        var renewAt=DateTime.UtcNow.AddSeconds(15);
        try
        {
            while(await timer.WaitForNextTickAsync(stoppingToken))
            {
                foreach(var s in sessions.Values.Where(s=>!s.Active && DateTime.UtcNow-s.Created>TimeSpan.FromSeconds(45)).ToArray()) await End(s,"expired");
                if(relayApi is not null && DateTime.UtcNow>=renewAt)
                {
                    renewAt=DateTime.UtcNow.AddSeconds(15);
                    foreach(var s in sessions.Values.Where(s=>s.Active).ToArray())
                    {
                        try {using var response=await relayApi.PutAsync("sessions/"+s.Id,null,stoppingToken);response.EnsureSuccessStatusCode();}
                        catch(Exception ex) when(ex is HttpRequestException or TaskCanceledException) {await End(s,"relay lost");}
                    }
                }
            }
        }
        catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested) { }
    }
    public async Task Run(WebSocket socket, CancellationToken aborted)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(aborted);
        var peer = new Peer(socket,lifetime.Token);
        try
        {
            lifetime.CancelAfter(TimeSpan.FromSeconds(15));
            var registration = await Receive(socket,lifetime.Token);
            if(registration?.Type != "register" || registration.PublicKey is null || registration.PublicKey.Length > 2048) throw new InvalidDataException("registration");
            using var rsa = RSA.Create();
            rsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(registration.PublicKey),out _);
            if(rsa.KeySize < 2048 || rsa.KeySize > 4096) throw new InvalidDataException("key size");
            peer.Name = (registration.Name ?? "RemoteDesk")[..Math.Min((registration.Name ?? "RemoteDesk").Length,80)];
            var nonce = RandomNumberGenerator.GetBytes(32);
            await peer.Send(new() {Type="challenge",Nonce=Convert.ToBase64String(nonce)});
            var auth = await Receive(socket,lifetime.Token);
            if(auth?.Type != "auth" || auth.Signature is null || !rsa.VerifyData(nonce,Convert.FromBase64String(auth.Signature),HashAlgorithmName.SHA256,RSASignaturePadding.Pss)) throw new InvalidDataException("authentication");
            // Persist only after proof of possession; canonical exported key prevents alternate encodings.
            peer.Id = Identity(Convert.ToBase64String(rsa.ExportSubjectPublicKeyInfo()));
            peer.PublicKey=Convert.ToBase64String(rsa.ExportSubjectPublicKeyInfo());
            if(!peers.TryAdd(peer.Id,peer)) throw new InvalidDataException("device already online");
            lifetime.CancelAfter(Timeout.InfiniteTimeSpan);
            log.LogInformation("Device connected {DeviceId}",peer.Id);
            await peer.Send(new() {Type="ready",DeviceId=peer.Id});
            while(socket.State == WebSocketState.Open)
            {
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                deadline.CancelAfter(TimeSpan.FromSeconds(60));
                var message = await Receive(socket,deadline.Token);
                if(message is null) break;
                if(!peer.Allow(message.Type)) { await peer.Send(new() {Type="error",Error="rate limit"}); continue; }
                await Handle(peer,message);
                foreach(var expired in sessions.Values.Where(s=>!s.Active && s.Created < DateTime.UtcNow.AddSeconds(-45)).ToArray()) await End(expired,"expired");
            }
        }
        catch(Exception ex) when(ex is WebSocketException or OperationCanceledException or InvalidDataException or JsonException or FormatException or CryptographicException)
        { log.LogInformation("Connection ended: {Reason}",ex.Message); }
        finally
        {
            if(peer.Id is not null && peers.TryGetValue(peer.Id,out var current) && ReferenceEquals(current,peer)) peers.TryRemove(peer.Id,out _);
            foreach(var session in sessions.Values.Where(s=>s.Host==peer || s.Viewer==peer).ToArray()) await End(session,"disconnected");
            lifetime.Cancel();
            if(socket.State==WebSocketState.CloseReceived) await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure,"RemoteDesk",CancellationToken.None);
        }
    }
    async Task Handle(Peer p, Envelope m)
    {
        if(m.Type=="heartbeat") { await p.Send(new(){Type="pong"}); return; }
        if(m.Type=="access-policy"){p.PasswordAccess=m.PasswordAccess;await p.Send(new(){Type="access-policy",PasswordAccess=p.PasswordAccess,RequestId=m.RequestId});return;}
        if(m.Type=="presence")
        {
            if(m.Payload is null||m.Payload.Length>4096){await p.Send(new(){Type="error",Error="invalid presence request"});return;}
            var ids=JsonSerializer.Deserialize<string[]>(m.Payload,Wire.Json);
            if(ids is null||ids.Length>200||ids.Any(id=>id is null||id.Length!=9||id.Any(c=>!char.IsAsciiDigit(c)))){await p.Send(new(){Type="error",Error="invalid presence IDs"});return;}
            var result=ids.Distinct().Select(id=>peers.TryGetValue(id,out var device)&&device.Online?new DevicePresence(id,device.Name,true):new DevicePresence(id,"",false)).ToArray();
            await p.Send(new(){Type="presence",Payload=JsonSerializer.Serialize(result,Wire.Json)});return;
        }
        if(m.Type=="discover")
        {
            if(m.RequestId?.Length>128) {await p.Send(new(){Type="error",Error="invalid discovery request"});return;}
            // Only authenticated peers of this broker; never probe addresses or the LAN.
            var devices=peers.Values.Where(other=>other!=p && other.Id is not null && other.Online)
                .OrderBy(other=>other.Id,StringComparer.Ordinal).Take(200)
                .Select(other=>new DeviceSummary(other.Id!,other.Name)).ToArray();
            var payload=JsonSerializer.Serialize(devices,Wire.Json);
            if(System.Text.Encoding.UTF8.GetByteCount(payload)>1024*1024) {await p.Send(new(){Type="error",Error="device list limit"});return;}
            await p.Send(new(){Type="devices",RequestId=m.RequestId,Payload=payload});return;
        }
        if(m.Type=="request")
        {
            if(m.TargetId==p.Id || m.TargetId is null || !peers.TryGetValue(m.TargetId,out var host) || m.Permissions is null || !m.Permissions.View || !m.Permissions.IsSubsetOf(new(true,true,true))) { await p.Send(new(){Type="error",Error="invalid or offline target"}); return; }
            Session? s=null;
            lock(sessionLock)
            {
                if(!sessions.Values.Any(s=>s.Host==p || s.Viewer==p || s.Host==host || s.Viewer==host))
                { s=new Session(Guid.NewGuid().ToString("N"),host,p,m.Permissions){PasswordAccess=m.PasswordAccess||host.PasswordAccess}; sessions[s.Id]=s; }
            }
            if(s is null) {await p.Send(new(){Type="error",Error="device busy"});return;}
            await p.Send(new(){Type="pending",SessionId=s.Id,DeviceId=host.Id,PasswordAccess=s.PasswordAccess});
            await host.Send(new(){Type="incoming",SessionId=s.Id,DeviceId=p.Id,Name=p.Name,Permissions=s.Requested,PasswordAccess=s.PasswordAccess}); return;
        }
        if(m.SessionId is null || !sessions.TryGetValue(m.SessionId,out var session) || (p!=session.Host && p!=session.Viewer)) { await p.Send(new(){Type="error",Error="invalid session"}); return; }
        if(m.Type is "end" or "cancel" || (m.Type=="reject" && p==session.Host)) { await End(session,session.PasswordAccess&&m.Type=="reject"?"password access denied":m.Type); return; }
        if(m.Type=="password-challenge"&&p==session.Host&&!session.Active&&!session.Challenged)
        {
            if(m.Nonce?.Length!=44||Convert.FromBase64String(m.Nonce).Length!=32){await End(session,"invalid challenge");return;}
            session.PasswordAccess=true;session.Challenged=true;
            await session.Viewer.Send(new(){Type="password-challenge",SessionId=session.Id,DeviceId=p.Id,Nonce=m.Nonce,PublicKey=p.PublicKey});return;
        }
        if(m.Type=="password-proof"&&p==session.Viewer&&!session.Active&&session.PasswordAccess&&session.Challenged&&!session.ProofSent)
        {
            if(m.Payload is null||m.Payload.Length>1024){await End(session,"invalid proof");return;}
            session.ProofSent=true;await session.Host.Send(new(){Type="password-proof",SessionId=session.Id,DeviceId=p.Id,Payload=m.Payload});return;
        }
        if(m.Type=="respond" && p==session.Host && !session.Active)
        {
            if(session.PasswordAccess&&!session.ProofSent){await End(session,"password proof required");return;}
            if(DateTime.UtcNow-session.Created>TimeSpan.FromSeconds(45) || m.Permissions is null || !m.Permissions.View || !m.Permissions.IsSubsetOf(session.Requested)) { await End(session,"invalid permission"); return; }
            session.Granted=m.Permissions;
            RelayTickets? tickets=null;
            if(relayApi is not null)
            {
                try
                {
                    using var response=await relayApi.PostAsJsonAsync("sessions",new RelayRegistration(session.Id,session.Granted),Wire.Json);
                    response.EnsureSuccessStatusCode();tickets=await response.Content.ReadFromJsonAsync<RelayTickets>(Wire.Json);
                    if(tickets is null) throw new HttpRequestException("empty relay response");
                }
                catch(Exception ex) when(ex is HttpRequestException or TaskCanceledException or JsonException) {await End(session,"relay unavailable");return;}
            }
            if(!sessions.ContainsKey(session.Id)) {await RevokeRelay(session.Id);return;}
            session.Active=true;
            await session.Host.Send(new(){Type="started",SessionId=session.Id,DeviceId=session.Viewer.Id,Name=session.Viewer.Name,Permissions=session.Granted,RelayUrl=relayUrl,RelayTicket=tickets?.HostTicket,IsHost=true});
            await session.Viewer.Send(new(){Type="started",SessionId=session.Id,DeviceId=session.Host.Id,Name=session.Host.Name,Permissions=session.Granted,RelayUrl=relayUrl,RelayTicket=tickets?.ViewerTicket,IsHost=false});
            log.LogInformation("Session authorized {SessionId}",session.Id); return;
        }
        if(!session.Active) {await p.Send(new(){Type="error",Error="session not authorized"}); return;}
        bool valid = relayApi is null && MediaPolicy.Valid(m,p==session.Host,session.Granted);
        if(!valid) { await p.Send(new(){Type="error",Error="invalid or unauthorized message"}); return; }
        await (p==session.Host?session.Viewer:session.Host).Send(m with {DeviceId=p.Id,PublicKey=null,Signature=null,Nonce=null});
    }
    async Task RevokeRelay(string sessionId)
    {
        if(relayApi is null) return;
        try {using var response=await relayApi.DeleteAsync("sessions/"+Uri.EscapeDataString(sessionId));response.EnsureSuccessStatusCode();}
        catch(Exception ex) when(ex is HttpRequestException or TaskCanceledException) {log.LogWarning("Relay revocation failed for {SessionId}",sessionId);}
    }
    async Task End(Session session,string reason)
    {
        if(!sessions.TryRemove(session.Id,out _)) return;
        await RevokeRelay(session.Id);
        await session.Host.Send(new(){Type="ended",SessionId=session.Id,Error=reason});
        await session.Viewer.Send(new(){Type="ended",SessionId=session.Id,Error=reason});
        log.LogInformation("Session ended {SessionId}: {Reason}",session.Id,reason);
    }
    static async Task<Envelope?> Receive(WebSocket socket,CancellationToken token)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[16384];
        WebSocketReceiveResult result;
        do
        {
            result=await socket.ReceiveAsync(new ArraySegment<byte>(chunk),token);
            if(result.MessageType==WebSocketMessageType.Close) return null;
            if(result.MessageType!=WebSocketMessageType.Text || buffer.Length+result.Count>Wire.MaxMessageBytes) throw new InvalidDataException("message bounds");
            buffer.Write(chunk,0,result.Count);
        } while(!result.EndOfMessage);
        return Wire.Decode(buffer.ToArray());
    }
    sealed class Peer(WebSocket socket,CancellationToken token)
    {
        readonly SemaphoreSlim sendLock = new(1,1);
        DateTime window=DateTime.UtcNow; int requests, messages;
        public string? Id; public string? PublicKey; public string Name="RemoteDesk";public bool PasswordAccess;
        public bool Online => socket.State==WebSocketState.Open && !token.IsCancellationRequested;
        public bool Allow(string type)
        {
            if(DateTime.UtcNow-window>TimeSpan.FromSeconds(1)) {window=DateTime.UtcNow;requests=messages=0;}
            return ++messages<=240 && (type is not ("request" or "discover" or "presence") || ++requests<=3);
        }
        public async Task Send(Envelope m)
        {
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            try { await sendLock.WaitAsync(timeout.Token); try {if(socket.State==WebSocketState.Open) await socket.SendAsync(Wire.Encode(m),WebSocketMessageType.Text,true,timeout.Token);} finally {sendLock.Release();} }
            catch(Exception ex) when(ex is WebSocketException or OperationCanceledException) { }
        }
    }
    sealed class Session(string id,Peer host,Peer viewer,Permissions requested)
    {
        public string Id=id; public Peer Host=host,Viewer=viewer; public Permissions Requested=requested,Granted=new(); public bool Active; public DateTime Created=DateTime.UtcNow;
        public bool PasswordAccess,Challenged,ProofSent;
    }
}
