using System.Net.WebSockets;
using System.Net.Http.Json;
using System.Security.Cryptography;
using RemoteDesk.Shared;
var signalUrl=args.FirstOrDefault()??"ws://127.0.0.1:5092/ws";
using var a=new ClientWebSocket();using var b=new ClientWebSocket();
async Task Send(ClientWebSocket s,Envelope m)=>await s.SendAsync(Wire.Encode(m),WebSocketMessageType.Text,true,CancellationToken.None);
async Task<Envelope> Read(ClientWebSocket s)
{
    using var timeout=new CancellationTokenSource(5000);using var stream=new MemoryStream();var buffer=new byte[16384];WebSocketReceiveResult r;
    do{r=await s.ReceiveAsync(new ArraySegment<byte>(buffer),timeout.Token);if(r.MessageType==WebSocketMessageType.Close)throw new WebSocketException("closed");stream.Write(buffer,0,r.Count);}while(!r.EndOfMessage);
    return Wire.Decode(stream.ToArray())!;
}
void Check(bool value,string label){if(!value)throw new Exception(label);Console.WriteLine("PASS "+label);}
async Task<string> Login(ClientWebSocket ws)
{
    using var rsa=RSA.Create(2048);await ws.ConnectAsync(new Uri(signalUrl),CancellationToken.None);
    await Send(ws,new(){Type="register",Name="Relay test",PublicKey=Convert.ToBase64String(rsa.ExportSubjectPublicKeyInfo())});var challenge=await Read(ws);
    await Send(ws,new(){Type="auth",Signature=Convert.ToBase64String(rsa.SignData(Convert.FromBase64String(challenge.Nonce!),HashAlgorithmName.SHA256,RSASignaturePadding.Pss))});
    return (await Read(ws)).DeviceId!;
}
async Task<ClientWebSocket> Attach(Envelope started)
{
    var ws=new ClientWebSocket();await ws.ConnectAsync(new Uri(started.RelayUrl!),CancellationToken.None);
    await Send(ws,new(){Type="attach",SessionId=started.SessionId,Payload=started.RelayTicket});
    Check((await Read(ws)).Type=="attached","ticket attachment");return ws;
}
await Login(a);var target=await Login(b);
await Send(a,new(){Type="request",TargetId=target,Permissions=new(true,true,true)});var pending=await Read(a);var incoming=await Read(b);
await Send(b,new(){Type="respond",SessionId=incoming.SessionId,Permissions=new(true,true,true)});
var viewerStarted=await Read(a);var hostStarted=await Read(b);
Check(viewerStarted.RelayTicket is not null && hostStarted.RelayTicket is not null && viewerStarted.RelayTicket!=hostStarted.RelayTicket,"separate role tickets");
using var viewer=await Attach(viewerStarted);using var host=await Attach(hostStarted);
if(args.Contains("--lease"))
{
    for(var i=0;i<7;i++)
    {
        await Task.Delay(10000);
        foreach(var ws in new[]{a,b,viewer,host}) {await Send(ws,new(){Type="heartbeat"});Check((await Read(ws)).Type=="pong","lease renewal heartbeat "+(i+1));}
    }
    Console.WriteLine("PASS signaling renews media authorization beyond original 60-second lease");
}
await Send(viewer,new(){Type="frame",SessionId=pending.SessionId,Payload="dGVzdA=="});Check((await Read(viewer)).Type=="error","wrong frame direction denied");
await Send(host,new(){Type="frame",SessionId=pending.SessionId,Payload="dGVzdA=="});Check((await Read(viewer)).Type=="frame","screen via separate relay");
await Send(viewer,new(){Type="input",SessionId=pending.SessionId,Data=new("move",.5,.5)});Check((await Read(host)).Type=="input","input via separate relay");
foreach(int level in new[]{0,50,100}){await Send(viewer,new(){Type="quality",SessionId=pending.SessionId,QualityLevel=level});var quality=await Read(host);Check(quality.Type=="quality"&&quality.QualityLevel==level,"live quality via separate relay "+level);}
string probe=Guid.NewGuid().ToString("N");await Send(viewer,new(){Type="probe",SessionId=pending.SessionId,Payload=probe});Check((await Read(host)).Payload==probe,"session latency probe routed to host");await Send(host,new(){Type="probe-reply",SessionId=pending.SessionId,Payload=probe});Check((await Read(viewer)).Payload==probe,"session latency reply routed to viewer");
await Send(viewer,new(){Type="probe-reply",SessionId=pending.SessionId,Payload=probe});Check((await Read(viewer)).Type=="error","latency reply direction enforced");await Send(viewer,new(){Type="probe",SessionId=pending.SessionId,Payload="invalid"});Check((await Read(viewer)).Type=="error","latency probe format enforced");
foreach(int level in new[]{-1,101}){await Send(viewer,new(){Type="quality",SessionId=pending.SessionId,QualityLevel=level});Check((await Read(viewer)).Type=="error","quality bounds enforced "+level);}
await Send(host,new(){Type="quality",SessionId=pending.SessionId,QualityLevel=50});Check((await Read(host)).Type=="error","quality direction enforced");
await Send(viewer,new(){Type="file",SessionId=pending.SessionId,File=new("offer","transfer","example.txt",4)});Check((await Read(host)).Type=="file","file via separate relay");
string browseId=Guid.NewGuid().ToString("N");
await Send(viewer,new(){Type="file",SessionId=pending.SessionId,File=new("browse",browseId,Path:"")});Check((await Read(host)).File?.Kind=="browse","remote browsing via relay");
await Send(host,new(){Type="file",SessionId=pending.SessionId,File=new("listing",browseId,Path:"",Entries:[])});Check((await Read(viewer)).File?.Kind=="listing","directory listing via relay");
await Send(host,new(){Type="file",SessionId=pending.SessionId,File=new("browse",browseId,Path:"")});Check((await Read(host)).Type=="error","host cannot initiate browsing");
using(var attack=new ClientWebSocket())
{
    await attack.ConnectAsync(new Uri(viewerStarted.RelayUrl!),CancellationToken.None);
    await Send(attack,new(){Type="attach",SessionId=pending.SessionId,Payload=viewerStarted.RelayTicket});
    bool denied=false;try{await Read(attack);}catch(WebSocketException){denied=true;}Check(denied,"used ticket denied");
}
await Send(host,new(){Type="frame",SessionId=pending.SessionId,Payload="dGVzdA=="});Check((await Read(viewer)).Type=="frame","ticket replay preserves valid session");
await Send(a,new(){Type="end",SessionId=pending.SessionId});Check((await Read(a)).Type=="ended" && (await Read(b)).Type=="ended","server revocation signals end");
bool closed=false;try{await Read(viewer);}catch(WebSocketException){closed=true;}Check(closed,"server revokes media sockets");
using var api=new HttpClient();var relayApi=args.ElementAtOrDefault(1)??"http://127.0.0.1:5091";
var unauthorized=await api.DeleteAsync(relayApi+"/sessions/invalid");Check(unauthorized.StatusCode==System.Net.HttpStatusCode.Unauthorized,"private API secret enforced");
await a.CloseAsync(WebSocketCloseStatus.NormalClosure,"test",CancellationToken.None);await b.CloseAsync(WebSocketCloseStatus.NormalClosure,"test",CancellationToken.None);
if(args.Contains("--lease"))
{
    api.DefaultRequestHeaders.Authorization=new("Bearer",Environment.GetEnvironmentVariable("RemoteDesk__RelaySecret")??throw new Exception("test RelaySecret environment required"));
    var id=Guid.NewGuid().ToString("N");
    using var response=await api.PostAsJsonAsync(relayApi+"/sessions",new RelayRegistration(id,new(true)),Wire.Json);response.EnsureSuccessStatusCode();
    var tickets=await response.Content.ReadFromJsonAsync<RelayTickets>(Wire.Json);
    using var orphanHost=await Attach(new(){RelayUrl=hostStarted.RelayUrl,RelayTicket=tickets!.HostTicket,SessionId=id});
    using var orphanViewer=await Attach(new(){RelayUrl=hostStarted.RelayUrl,RelayTicket=tickets.ViewerTicket,SessionId=id});
    var start=DateTime.UtcNow;bool expired=false;
    for(var i=0;i<7;i++)
    {
        await Task.Delay(10000);
        try {foreach(var ws in new[]{orphanHost,orphanViewer}) {await Send(ws,new(){Type="heartbeat"});Check((await Read(ws)).Type=="pong","orphan heartbeat "+(i+1));}}
        catch(WebSocketException){expired=true;break;}
    }
    Check(expired && DateTime.UtcNow-start>=TimeSpan.FromSeconds(58),"authorization lease expires despite continued endpoint heartbeats");
}
Console.WriteLine("Separate relay integration passed.");
