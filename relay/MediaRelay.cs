using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text.Json;
using RemoteDesk.Shared;
namespace RemoteDesk.Relay;
public sealed class MediaRelay : BackgroundService
{
    readonly ConcurrentDictionary<string, Session> sessions=new();
    public RelayTickets? Create(RelayRegistration r)
    {
        var host=Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var viewer=Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        return sessions.TryAdd(r.SessionId,new(r.SessionId,r.Permissions,host,viewer)) ? new(host,viewer) : null;
    }
    public void Revoke(string id)
    {
        if(!sessions.TryRemove(id,out var s)) return;
        lock(s.Gate) {s.Closed=true;s.Ready.TrySetCanceled();s.Host?.Socket.Abort();s.Viewer?.Socket.Abort();}
    }
    public bool Renew(string id)
    {
        if(!sessions.TryGetValue(id,out var s)) return false;
        lock(s.Gate) {if(s.Closed)return false;Interlocked.Exchange(ref s.Lease,DateTime.UtcNow.AddSeconds(60).Ticks);return true;}
    }
    protected override async Task ExecuteAsync(CancellationToken token)
    {
        using var timer=new PeriodicTimer(TimeSpan.FromSeconds(1));
        try
        {
            while(await timer.WaitForNextTickAsync(token))
                foreach(var s in sessions.Values)
                    if(DateTime.UtcNow.Ticks>Interlocked.Read(ref s.Lease) || (DateTime.UtcNow-s.Created>TimeSpan.FromSeconds(45) && (s.Host is null || s.Viewer is null)) || s.Host?.Expired==true || s.Viewer?.Expired==true) Revoke(s.Id);
        } catch(OperationCanceledException) when(token.IsCancellationRequested) { }
    }
    public override Task StopAsync(CancellationToken cancellationToken) {foreach(var id in sessions.Keys) Revoke(id);return base.StopAsync(cancellationToken);}
    public async Task Run(WebSocket ws,CancellationToken token)
    {
        Session? session=null; Peer? peer=null;
        try
        {
            using var initial=CancellationTokenSource.CreateLinkedTokenSource(token); initial.CancelAfter(TimeSpan.FromSeconds(10));
            var attach=await Receive(ws,initial.Token);
            if(attach?.Type!="attach" || attach.SessionId is null || attach.Payload is null || !sessions.TryGetValue(attach.SessionId,out session)) throw new InvalidDataException();
            lock(session.Gate)
            {
                if(session.Closed || DateTime.UtcNow-session.Created>TimeSpan.FromSeconds(45)) throw new InvalidDataException();
                var isHost=SecureEqual(attach.Payload,session.HostTicket);
                var isViewer=SecureEqual(attach.Payload,session.ViewerTicket);
                if((!isHost && !isViewer) || (isHost && session.Host is not null) || (isViewer && session.Viewer is not null)) throw new InvalidDataException();
                peer=new(ws,isHost,token);
                if(isHost) {session.Host=peer;session.HostTicket=null;} else {session.Viewer=peer;session.ViewerTicket=null;}
            }
            await peer.Send(new(){Type="attached",SessionId=session.Id,Permissions=session.Permissions,IsHost=peer.IsHost});
            lock(session.Gate)
            {
                peer.Attached=true;
                if(session.Host?.Attached==true && session.Viewer?.Attached==true) session.Ready.TrySetResult(true);
            }
            while(ws.State==WebSocketState.Open)
            {
                using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);timeout.CancelAfter(TimeSpan.FromSeconds(60));
                var m=await Receive(ws,timeout.Token); if(m is null) break;
                peer.Touch();
                if(!peer.Allow()) {await peer.Send(new(){Type="error",Error="rate limit"});continue;}
                if(m.Type=="heartbeat") {await peer.Send(new(){Type="pong"});continue;}
                if(m.SessionId!=session.Id || !MediaPolicy.Valid(m,peer.IsHost,session.Permissions)) {await peer.Send(new(){Type="error",Error="invalid or unauthorized media"});continue;}
                // Do not drop the first frame/file offer while the other endpoint is attaching.
                await session.Ready.Task.WaitAsync(token);
                Peer? target;
                lock(session.Gate) {if(session.Closed) break;target=peer.IsHost?session.Viewer:session.Host;}
                if(target is not null) await target.Send(m with {RelayTicket=null,PublicKey=null,Signature=null,Nonce=null});
            }
        }
        catch(Exception ex) when(ex is WebSocketException or InvalidDataException or OperationCanceledException or JsonException) { }
        finally
        {
            // Invalid/reused tickets must never tear down the legitimate session.
            if(session is not null && peer is not null) Revoke(session.Id);
            if(ws.State==WebSocketState.CloseReceived) await ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure,"RemoteDesk",CancellationToken.None);
        }
    }
    static bool SecureEqual(string candidate,string? expected)
    {
        if(expected is null || candidate.Length>128) return false;
        return CryptographicOperations.FixedTimeEquals(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(candidate)),SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(expected)));
    }
    static async Task<Envelope?> Receive(WebSocket socket,CancellationToken token)
    {
        using var stream=new MemoryStream();var buffer=new byte[16384];WebSocketReceiveResult r;
        do {r=await socket.ReceiveAsync(new ArraySegment<byte>(buffer),token);if(r.MessageType==WebSocketMessageType.Close)return null;if(r.MessageType!=WebSocketMessageType.Text || stream.Length+r.Count>Wire.MaxMessageBytes)throw new InvalidDataException();stream.Write(buffer,0,r.Count);}while(!r.EndOfMessage);
        return Wire.Decode(stream.ToArray());
    }
    sealed class Session(string id,Permissions permissions,string hostTicket,string viewerTicket)
    {
        public readonly object Gate=new(); public string Id=id;public Permissions Permissions=permissions;
        public TaskCompletionSource<bool> Ready=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string? HostTicket=hostTicket,ViewerTicket=viewerTicket; public Peer? Host,Viewer;public bool Closed;public DateTime Created=DateTime.UtcNow;
        public long Lease=DateTime.UtcNow.AddSeconds(60).Ticks;
    }
    sealed class Peer(WebSocket socket,bool host,CancellationToken token)
    {
        public WebSocket Socket=socket;public bool IsHost=host;public bool Attached;
        readonly SemaphoreSlim gate=new(1,1);long last=DateTime.UtcNow.Ticks;DateTime window=DateTime.UtcNow;int count;
        public bool Expired=>DateTime.UtcNow-new DateTime(Interlocked.Read(ref last),DateTimeKind.Utc)>TimeSpan.FromSeconds(60);
        public void Touch()=>Interlocked.Exchange(ref last,DateTime.UtcNow.Ticks);
        public bool Allow(){if(DateTime.UtcNow-window>TimeSpan.FromSeconds(1)){window=DateTime.UtcNow;count=0;}return ++count<=240;}
        public async Task Send(Envelope m)
        {
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);timeout.CancelAfter(TimeSpan.FromSeconds(10));
            try{await gate.WaitAsync(timeout.Token);try{if(Socket.State==WebSocketState.Open)await Socket.SendAsync(Wire.Encode(m),WebSocketMessageType.Text,true,timeout.Token);}finally{gate.Release();}}catch(Exception ex)when(ex is WebSocketException or OperationCanceledException){Socket.Abort();}
        }
    }
}
