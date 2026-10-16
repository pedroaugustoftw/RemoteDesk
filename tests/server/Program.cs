using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text.Json;
using RemoteDesk.Shared;
var url=args.FirstOrDefault()??"ws://127.0.0.1:5090/ws";
using var rsa1=RSA.Create(2048); using var rsa2=RSA.Create(2048);
using var a=new ClientWebSocket(); using var b=new ClientWebSocket();
async Task Send(ClientWebSocket s,Envelope m)=>await s.SendAsync(Wire.Encode(m),WebSocketMessageType.Text,true,CancellationToken.None);
async Task<Envelope> Read(ClientWebSocket s)
{
    using var timeout=new CancellationTokenSource(5000); using var stream=new MemoryStream(); var buffer=new byte[16384]; WebSocketReceiveResult result;
    do { result=await s.ReceiveAsync(new ArraySegment<byte>(buffer),timeout.Token); stream.Write(buffer,0,result.Count); }while(!result.EndOfMessage);
    return Wire.Decode(stream.ToArray())!;
}
async Task<string> Login(ClientWebSocket s,RSA key)
{
    await s.ConnectAsync(new Uri(url),CancellationToken.None);
    await Send(s,new(){Type="register",PublicKey=Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()),Name="Test"});
    var challenge=await Read(s);
    await Send(s,new(){Type="auth",Signature=Convert.ToBase64String(key.SignData(Convert.FromBase64String(challenge.Nonce!),HashAlgorithmName.SHA256,RSASignaturePadding.Pss))});
    var ready=await Read(s); Check(ready.Type=="ready","RSA authentication"); return ready.DeviceId!;
}
void Check(bool value,string label) { if(!value) throw new Exception(label); Console.WriteLine("PASS "+label); }
var idA=await Login(a,rsa1); var idB=await Login(b,rsa2);
if(args.Contains("--discovery"))
{
    await Send(a,new(){Type="discover",RequestId="discovery-1"});var response=await Read(a);
    var devices=JsonSerializer.Deserialize<DeviceSummary[]>(response.Payload!,Wire.Json)!;
    Check(response.Type=="devices"&&response.RequestId=="discovery-1","discovery response correlation");
    Check(devices.Length==1&&devices[0].Id==idB&&devices[0].Name=="Test","discovery includes authenticated online peer, excludes requester");
    Check(devices.Length<=200&&System.Text.Encoding.UTF8.GetByteCount(response.Payload!)<=1024*1024,"discovery response bounds");
    using(var unauthenticated=new ClientWebSocket())
    {
        await unauthenticated.ConnectAsync(new Uri(url),CancellationToken.None);
        await Send(unauthenticated,new(){Type="discover"});using var deadline=new CancellationTokenSource(5000);var buffer=new byte[1024];
        bool denied;
        try {var received=await unauthenticated.ReceiveAsync(new ArraySegment<byte>(buffer),deadline.Token);denied=received.MessageType==WebSocketMessageType.Close;}
        catch(WebSocketException){denied=true;}
        Check(denied,"unauthenticated discovery denied");
    }
    await b.CloseAsync(WebSocketCloseStatus.NormalClosure,"test",CancellationToken.None);
    await Task.Delay(100);await Send(a,new(){Type="discover"});response=await Read(a);
    Check(JsonSerializer.Deserialize<DeviceSummary[]>(response.Payload!,Wire.Json)!.Length==0,"disconnected peer removed from discovery");
    await Task.Delay(1100);
    for(int i=0;i<4;i++){await Send(a,new(){Type="discover"});response=await Read(a);Check(response.Type==(i<3?"devices":"error"),"discovery rate bound "+i);}
    await a.CloseAsync(WebSocketCloseStatus.NormalClosure,"test",CancellationToken.None);
    Console.WriteLine("Discovery integration checks passed.");return;
}
await Send(a,new(){Type="request",TargetId=idB,Permissions=new(true,true,true)});
var pending=await Read(a); var incoming=await Read(b); Check(pending.SessionId==incoming.SessionId && incoming.Type=="incoming","explicit request");
await Send(a,new(){Type="input",SessionId=pending.SessionId,Data=new("move",.5,.5)});
Check((await Read(a)).Type=="error","input before consent denied");
await Send(b,new(){Type="respond",SessionId=pending.SessionId,Permissions=new(true,false,true)});
Check((await Read(a)).Type=="started" && (await Read(b)).Type=="started","permission subset accepted");
await Send(a,new(){Type="input",SessionId=pending.SessionId,Data=new("move",.5,.5)});
Check((await Read(a)).Type=="error","control denied");
await Send(b,new(){Type="frame",SessionId=pending.SessionId,Payload="dGVzdA=="});
Check((await Read(a)).Type=="frame","authorized screen relay");
await Send(a,new(){Type="file",SessionId=pending.SessionId,File=new("offer","transfer","test.txt",4)});
Check((await Read(b)).Type=="file","file offer relay");
await Send(a,new(){Type="file",SessionId=pending.SessionId,File=new("chunk","transfer",Chunk:Convert.ToBase64String(new byte[65536]))});
Check((await Read(b)).File?.Kind=="chunk","bounded file chunk relay");
await Send(a,new(){Type="file",SessionId=pending.SessionId,File=new("chunk","transfer",Chunk:Convert.ToBase64String(new byte[65537]))});
Check((await Read(a)).Type=="error","oversized file chunk denied");
await Send(a,new(){Type="end",SessionId=pending.SessionId});
Check((await Read(a)).Type=="ended" && (await Read(b)).Type=="ended","immediate session end");
await Send(a,new(){Type="request",TargetId=idB,Permissions=new(true)}); pending=await Read(a); incoming=await Read(b);
await Send(b,new(){Type="reject",SessionId=incoming.SessionId});
Check((await Read(a)).Type=="ended" && (await Read(b)).Type=="ended","reject request");
await Send(a,new(){Type="request",TargetId=idB,Permissions=new(true,true)}); pending=await Read(a); incoming=await Read(b);
await Send(b,new(){Type="respond",SessionId=incoming.SessionId,Permissions=new(true,true)});
Check((await Read(a)).Type=="started" && (await Read(b)).Type=="started","control consent");
await Send(a,new(){Type="input",SessionId=pending.SessionId,Data=new("key",Key:65,Down:true)});
Check((await Read(b)).Type=="input","authorized input relay");
await Send(a,new(){Type="input",SessionId=pending.SessionId,Data=new("move",2,0)});
Check((await Read(a)).Type=="error","input bounds");
await Send(a,new(){Type="frame",SessionId=pending.SessionId,Payload="dGVzdA=="});
Check((await Read(a)).Type=="error","frame direction");
await a.CloseAsync(WebSocketCloseStatus.NormalClosure,"test",CancellationToken.None);
Check((await Read(b)).Type=="ended","disconnect ends session");
using var c=new ClientWebSocket(); Check(await Login(c,rsa1)==idA,"persistent identity reconnect");
await c.CloseAsync(WebSocketCloseStatus.NormalClosure,"test",CancellationToken.None);
await b.CloseAsync(WebSocketCloseStatus.NormalClosure,"test",CancellationToken.None);
Console.WriteLine("Server integration checks passed.");
