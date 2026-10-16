using System.IO;
using System.Text.Json;
using System.Threading.Channels;
using System.Security.Cryptography;
using RemoteDesk.Client.Services;
using RemoteDesk.Client.Security;
using RemoteDesk.Shared;
if(args.Length<2)throw new ArgumentException("settings.json path and target ID required. --request optionally tests a visible request; no password or media is used.");
using var settings=JsonDocument.Parse(File.ReadAllText(args[0]));
var profile=new Profile{Server=settings.RootElement.GetProperty("Server").GetString()!,ServerCertificateSha256=settings.RootElement.GetProperty("ServerCertificateSha256").GetString()??"",Name="RemoteDesk diagnóstico autorizado"};
using var identity=RSA.Create(2048);await using var connection=new Connection(identity);var messages=Channel.CreateUnbounded<Envelope>();connection.Message+=m=>messages.Writer.TryWrite(m);
async Task<Envelope> Read(int seconds=6){using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(seconds));return await messages.Reader.ReadAsync(deadline.Token);}
await connection.Connect(profile);while((await Read()).Type!="ready"){}Console.WriteLine("PASS conexão autenticada com identidade temporária");
string request=Guid.NewGuid().ToString("N");await connection.Send(new(){Type="access-policy",RequestId=request,PasswordAccess=false});
var policy=await Read();Console.WriteLine($"Policy reply: {policy.Type}; request ID confirmed: {policy.RequestId==request}; error: {policy.Error}");
await connection.Send(new(){Type="presence",Payload=JsonSerializer.Serialize(new[]{args[1]},Wire.Json)});var presence=await Read();if(presence.Type=="presence"){var target=JsonSerializer.Deserialize<DevicePresence[]>(presence.Payload!,Wire.Json)!.Single();Console.WriteLine("Target online: "+target.Online);}else Console.WriteLine("Presence reply: "+presence.Type+" "+presence.Error);
if(args.Contains("--request"))
{
    string? session=null;
    try
    {
        await connection.Send(new(){Type="request",TargetId=args[1],Permissions=new(true,false,false),PasswordAccess=args.Contains("--password-mode")});
        for(int i=0;i<4;i++)
        {
            var message=await Read(12);Console.WriteLine($"Session reply: {message.Type}; password mode: {message.PasswordAccess}; challenge nonce present: {message.Nonce!=null}; authenticated public key present: {message.PublicKey!=null}; error: {message.Error}");session??=message.SessionId;
            if(message.Type=="password-challenge"&&args.Contains("--authenticate-saved"))
            {
                var saved=settings.RootElement.GetProperty("Devices").EnumerateArray().Single(d=>d.GetProperty("Id").GetString()==args[1]);
                string pinned=saved.TryGetProperty("KeySha256",out var fingerprint)?fingerprint.GetString()??"":"";
                string actual=Convert.ToHexString(SHA256.HashData(Convert.FromBase64String(message.PublicKey!)));if(pinned.Length>0&&pinned!=actual)throw new CryptographicException("A chave do destino mudou; diagnóstico recusou usar a senha salva.");
                string encrypted=saved.GetProperty("PasswordDpapi").GetString()??throw new InvalidOperationException("Sem senha salva.");
                string proof=PasswordAccess.EncryptProof(message.PublicKey!,new(session!,message.Nonce!,PasswordAccess.Unseal(encrypted)));
                await connection.Send(new(){Type="password-proof",SessionId=session,Payload=proof});Console.WriteLine("Prova cifrada enviada com a credencial já salva. Nenhuma senha foi exibida.");continue;
            }
            if(message.Type=="started")Console.WriteLine("PASS autenticação por senha aceita pelo destino; encerrando imediatamente sem anexar mídia, teclado, mouse ou arquivos.");
            if(message.Type=="ended")session=null;
            if(message.Type is "password-challenge" or "error" or "ended" or "started")break;
        }
    }
    catch(OperationCanceledException){Console.WriteLine("TIMEOUT aguardando desafio de senha ou autorização. Nenhuma senha, mídia ou entrada foi enviada.");}
    finally{if(session!=null){await connection.Send(new(){Type="end",SessionId=session});Console.WriteLine("Solicitação encerrada pelo diagnóstico.");}}
}
