using System.Net.WebSockets;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RemoteDesk.Client.Security;
using RemoteDesk.Shared;
var root=Path.Combine(Path.GetTempPath(),"RemoteDesk-access-test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
int passed=0;void Check(bool value,string label){if(!value)throw new Exception(label);passed++;Console.WriteLine("PASS "+label);}
const string password="Local-Remote-Test-7492!";
var policy=new PasswordAccess(root);Check(!policy.Available,"password access defaults disabled");
try{policy.Configure("short",new(true));throw new Exception("short password accepted");}catch(ArgumentException){passed++;Console.WriteLine("PASS minimum password length");}
policy.Configure(password,new(true,false,true));Check(policy.Available,"explicit host opt-in enabled");
var reloaded=new PasswordAccess(root);Check(reloaded.Read()?.Enabled==true,"enabled password policy survives new instance");var beforeUpdate=reloaded.Read()!;reloaded.UpdatePermissions(new(true,false,true));Check(reloaded.Read()!.Verifier==beforeUpdate.Verifier&&reloaded.Verify(new("keep","nonce",password),"keep","nonce")!=null,"permission-only save keeps existing password verifier");
Check(!Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(root,"password-access.dpapi"))).Contains(password),"host secret not stored plaintext");
var grant=policy.Verify(new("session","nonce",password),"session","nonce");Check(grant==new Permissions(true,false,true),"correct password returns only configured permissions");
Check(policy.Verify(new("old-session","old-nonce",password),"session","nonce") is null,"proof bound to session and nonce");
string saved=PasswordAccess.Seal(password);Check(saved!=password&&PasswordAccess.Unseal(saved)==password,"saved password DPAPI roundtrip");
using(var smallKey=RSA.Create(2048))
{
    string unicode=string.Concat(Enumerable.Repeat("🔵",24));string nonce=Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    string encrypted=PasswordAccess.EncryptProof(Convert.ToBase64String(smallKey.ExportSubjectPublicKeyInfo()),new(Guid.NewGuid().ToString("N"),nonce,unicode));
    var clear=smallKey.Decrypt(Convert.FromBase64String(encrypted),RSAEncryptionPadding.OaepSHA256);try{Check(PasswordAccess.DecodeProof(clear).Password==unicode,"96-byte Unicode password fits RSA-2048 envelope");}finally{CryptographicOperations.ZeroMemory(clear);}
}
for(int i=0;i<5;i++)policy.Verify(new("session","nonce","incorrect"),"session","nonce");
Check(!new PasswordAccess(root).Available,"failed attempts persist host lockout");
policy.Disable();Check(!policy.Available&&policy.Read() is null,"disable removes host authorization");policy.Configure(password,new(true,false,false));
if(args.Length==0){Console.WriteLine($"{passed} access checks passed; network tests skipped.");return;}
using var hostKey=RSA.Create(3072);using var viewerKey=RSA.Create(3072);using var host=new ClientWebSocket();using var viewer=new ClientWebSocket();
async Task Send(ClientWebSocket ws,Envelope m)=>await ws.SendAsync(Wire.Encode(m),WebSocketMessageType.Text,true,CancellationToken.None);
async Task<Envelope> Read(ClientWebSocket ws)
{
    using var deadline=new CancellationTokenSource(6000);using var data=new MemoryStream();var buffer=new byte[16384];ValueWebSocketReceiveResult item;
    do{item=await ws.ReceiveAsync(buffer.AsMemory(),deadline.Token);if(item.MessageType==WebSocketMessageType.Close)throw new Exception("unexpected close");data.Write(buffer,0,item.Count);}while(!item.EndOfMessage);return Wire.Decode(data.ToArray())!;
}
async Task<string> Login(ClientWebSocket ws,RSA key)
{
    await ws.ConnectAsync(new Uri(args[0]),CancellationToken.None);await Send(ws,new(){Type="register",PublicKey=Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()),Name="Access test"});var challenge=await Read(ws);
    await Send(ws,new(){Type="auth",Signature=Convert.ToBase64String(key.SignData(Convert.FromBase64String(challenge.Nonce!),HashAlgorithmName.SHA256,RSASignaturePadding.Pss))});var ready=await Read(ws);Check(ready.Type=="ready","device RSA authentication");return ready.DeviceId!;
}
string hostId=await Login(host,hostKey),viewerId=await Login(viewer,viewerKey);
await Send(host,new(){Type="access-policy",PasswordAccess=true});Check((await Read(host)).PasswordAccess,"host advertises password opt-in");
await Send(viewer,new(){Type="presence",Payload=JsonSerializer.Serialize(new[]{hostId,"999999999"},Wire.Json)});var statuses=JsonSerializer.Deserialize<DevicePresence[]>((await Read(viewer)).Payload!,Wire.Json)!;
Check(statuses.Any(d=>d.Id==hostId&&d.Online)&&statuses.Any(d=>d.Id=="999999999"&&!d.Online),"registered devices online/offline presence");
await Send(viewer,new(){Type="presence",Payload=JsonSerializer.Serialize(Enumerable.Repeat(hostId,201).ToArray(),Wire.Json)});Check((await Read(viewer)).Type=="error","presence bounded to 200 IDs");
string? previousProof=null;
for(int attempt=0;attempt<3;attempt++)
{
    await Task.Delay(1100);
    await Send(viewer,new(){Type="request",TargetId=hostId,Permissions=new(true,true,true),PasswordAccess=attempt!=0});var pending=await Read(viewer);var incoming=await Read(host);
    Check(incoming.PasswordAccess&&pending.PasswordAccess&&pending.Type=="pending",attempt==0?"normal connect automatically detects target password policy":"password mode reaches host");string nonce=Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    await Send(host,new(){Type="password-challenge",SessionId=pending.SessionId,Nonce=nonce,PublicKey="spoofed"});var challenge=await Read(viewer);
    Check(challenge.PublicKey==Convert.ToBase64String(hostKey.ExportSubjectPublicKeyInfo()),"challenge uses authenticated host key");
    string proof=attempt==2?previousProof!:PasswordAccess.EncryptProof(challenge.PublicKey!,new(pending.SessionId!,nonce,attempt==1?"incorrect":password));
    if(attempt==0)previousProof=proof;
    Check(!proof.Contains(password),"password is encrypted before signaling");
    await Send(viewer,new(){Type="password-proof",SessionId=pending.SessionId,Payload=proof});var routed=await Read(host);
    var plaintext=hostKey.Decrypt(Convert.FromBase64String(routed.Payload!),RSAEncryptionPadding.OaepSHA256);Permissions? permitted;
    try{permitted=policy.Verify(PasswordAccess.DecodeProof(plaintext),pending.SessionId!,nonce);}finally{CryptographicOperations.ZeroMemory(plaintext);}
    if(attempt==0)
    {
        await Send(viewer,new(){Type="password-proof",SessionId=pending.SessionId,Payload=proof});Check((await Read(viewer)).Type=="error","duplicate proof not forwarded twice");
        Check(permitted==new Permissions(true,false,false),"host checks password and narrows permissions without prompt");
        await Send(host,new(){Type="respond",SessionId=pending.SessionId,Permissions=permitted});var hostStarted=await Read(host);var viewerStarted=await Read(viewer);
        Check(hostStarted.Type=="started"&&viewerStarted.Type=="started"&&!viewerStarted.Permissions!.Control&&!viewerStarted.Permissions.Files,"only host-approved permissions become active");
        await Send(viewer,new(){Type="input",SessionId=pending.SessionId,Data=new("move",.5,.5)});Check((await Read(viewer)).Type=="error","control blocked when host policy denies it");
        await Send(viewer,new(){Type="end",SessionId=pending.SessionId});Check((await Read(viewer)).Type=="ended"&&(await Read(host)).Type=="ended","authenticated session ends immediately");
    }
    else
    {
        Check(permitted is null,attempt==1?"wrong password denied":"captured proof replay denied in new session");
        await Send(host,new(){Type="reject",SessionId=pending.SessionId});Check((await Read(viewer)).Type=="ended"&&(await Read(host)).Type=="ended","failed password never starts session");
    }
}
await Task.Delay(1100);await Send(viewer,new(){Type="request",TargetId=hostId,Permissions=new(true),PasswordAccess=true});var unproved=await Read(viewer);await Read(host);
await Send(host,new(){Type="respond",SessionId=unproved.SessionId,Permissions=new(true)});Check((await Read(viewer)).Type=="ended"&&(await Read(host)).Type=="ended","password session cannot start before proof exchange");
await Send(host,new(){Type="access-policy",PasswordAccess=false});Check(!(await Read(host)).PasswordAccess,"host can disable password advertisement");
await Task.Delay(1100);await Send(viewer,new(){Type="request",TargetId=hostId,Permissions=new(true)});var manualPending=await Read(viewer);var manualIncoming=await Read(host);Check(!manualPending.PasswordAccess&&!manualIncoming.PasswordAccess,"disabled password uses visible local authorization flow");
await Send(host,new(){Type="reject",SessionId=manualIncoming.SessionId});Check((await Read(viewer)).Error=="reject"&&(await Read(host)).Type=="ended","manual refusal is explicitly reported to requester");
await Task.Delay(1100);await Send(viewer,new(){Type="request",TargetId=hostId,Permissions=new(true)});var raced=await Read(viewer);await Read(host);await Send(host,new(){Type="password-challenge",SessionId=raced.SessionId,Nonce=Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))});Check((await Read(viewer)).Type=="password-challenge","host can enforce password even before policy advertisement");await Send(host,new(){Type="respond",SessionId=raced.SessionId,Permissions=new(true)});Check((await Read(viewer)).Type=="ended"&&(await Read(host)).Type=="ended","password enforcement upgrade still requires proof");
await viewer.CloseAsync(WebSocketCloseStatus.NormalClosure,"done",CancellationToken.None);await Task.Delay(100);
await Send(host,new(){Type="presence",Payload=JsonSerializer.Serialize(new[]{viewerId},Wire.Json)});Check(!JsonSerializer.Deserialize<DevicePresence[]>((await Read(host)).Payload!,Wire.Json)![0].Online,"offline transition reflected");
await host.CloseAsync(WebSocketCloseStatus.NormalClosure,"done",CancellationToken.None);
Console.WriteLine($"{passed} access checks passed. No capture, file or input injection performed.");
