using System.Net.WebSockets;
using System.IO;
using System.Security.Cryptography;
using RemoteDesk.Shared;
namespace RemoteDesk.Client.Services;
public sealed class Connection : IAsyncDisposable
{
    private readonly RSA key;
    public RemoteDesk.Client.Security.PasswordProof DecryptProof(string encrypted)
    {
        byte[] clear;lock(key)clear=key.Decrypt(Convert.FromBase64String(encrypted),RSAEncryptionPadding.OaepSHA256);
        try{return RemoteDesk.Client.Security.PasswordAccess.DecodeProof(clear);}
        finally{CryptographicOperations.ZeroMemory(clear);}
    }
    public Connection(RSA? identity = null) { key = identity ?? Profile.LoadKey(); }
    private readonly SemaphoreSlim sendLock = new(1, 1);
    private ClientWebSocket? socket;
    private CancellationTokenSource? lifetime;
    public event Action<Envelope>? Message;
    public event Action<string>? State;
    public bool Ready { get; private set; }
    public async Task Connect(Profile profile)
    {
        await Disconnect();
        var uri = new Uri(profile.Server);
        if (uri.Scheme != "wss" && !(uri.Scheme == "ws" && uri.IsLoopback)) throw new InvalidOperationException("Use wss:// com certificado válido para conexões de rede.");
        socket = new ClientWebSocket(); ConfigureCertificate(socket, profile.ServerCertificateSha256); lifetime = new CancellationTokenSource();
        var authenticated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        State?.Invoke("Conectando ao servidor…");
        using var connectDeadline=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);connectDeadline.CancelAfter(TimeSpan.FromSeconds(15));
        await socket.ConnectAsync(uri, connectDeadline.Token);
        _ = Receive(socket, lifetime.Token, authenticated);
        await Send(new Envelope { Type = "register", PublicKey = Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()), Name = profile.Name });
        _ = Heartbeat(lifetime.Token);
        await authenticated.Task.WaitAsync(TimeSpan.FromSeconds(15), lifetime.Token);
    }
    public async Task Send(Envelope envelope)
    {
        var s = socket; var token = lifetime?.Token ?? CancellationToken.None;
        if (s?.State != WebSocketState.Open) throw new IOException("Servidor desconectado.");
        var bytes = Wire.Encode(envelope);
        if (bytes.Length > Wire.MaxMessageBytes) throw new IOException("Mensagem excedeu limite.");
        await sendLock.WaitAsync(token);
        try { using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(10)); await s.SendAsync(bytes.AsMemory(), WebSocketMessageType.Text, true, timeout.Token); }
        finally { sendLock.Release(); }
    }
    public static void ConfigureCertificate(ClientWebSocket socket, string fingerprint)
    {
        if(string.IsNullOrWhiteSpace(fingerprint))return;
        byte[] expected=Convert.FromHexString(fingerprint.Replace(" ","").Replace(":",""));
        if(expected.Length!=32)throw new ArgumentException("Impressão SHA-256 deve ter 64 dígitos hexadecimais.");
        socket.Options.RemoteCertificateValidationCallback=(_,certificate,_,_)=>certificate!=null&&CryptographicOperations.FixedTimeEquals(SHA256.HashData(certificate.GetRawCertData()),expected);
    }
    private async Task Receive(ClientWebSocket s, CancellationToken token, TaskCompletionSource authenticated)
    {
        string reason="Desconectado";
        try
        {
            byte[] buffer = new byte[65536];
            while (!token.IsCancellationRequested)
            {
                using var ms = new MemoryStream(); ValueWebSocketReceiveResult read;
                do { read = await s.ReceiveAsync(buffer.AsMemory(), token); if (read.MessageType == WebSocketMessageType.Close) return; if (read.MessageType != WebSocketMessageType.Text || ms.Length + read.Count > Wire.MaxMessageBytes) throw new IOException("Mensagem inválida."); ms.Write(buffer, 0, read.Count); } while (!read.EndOfMessage);
                var msg = Wire.Decode(ms.ToArray()) ?? throw new IOException("Mensagem vazia.");
                if (msg.Type == "challenge") await Send(new Envelope { Type = "auth", Signature = Convert.ToBase64String(key.SignData(Convert.FromBase64String(msg.Nonce ?? ""), HashAlgorithmName.SHA256, RSASignaturePadding.Pss)) });
                else if (msg.Type == "ready") { Ready = true; State?.Invoke("Disponível"); Message?.Invoke(msg); authenticated.TrySetResult(); }
                else {if(msg.Type=="error"&&!Ready)authenticated.TrySetException(new IOException(msg.Error));Message?.Invoke(msg);}
            }
        }
        catch (Exception e) when (e is IOException or WebSocketException or OperationCanceledException or System.Text.Json.JsonException or FormatException) { reason=e.Message; }
        finally { if(!Ready&&!token.IsCancellationRequested){reason="Não foi possível registrar o dispositivo. Confira o servidor de sinalização (5081), não o relay (5091).";authenticated.TrySetException(new IOException(reason));}else authenticated.TrySetCanceled(); Ready = false; s.Abort(); if (ReferenceEquals(s, socket)) { State?.Invoke(reason); Message?.Invoke(new Envelope { Type = "disconnected", Error=reason }); } }
    }
    private async Task Heartbeat(CancellationToken token)
    {
        try { while (!token.IsCancellationRequested) { await Task.Delay(15000, token); await Send(new Envelope { Type = "heartbeat" }); } } catch (Exception) { }
    }
    public Task Disconnect() { Ready = false; lifetime?.Cancel(); socket?.Abort(); socket?.Dispose(); socket = null; lifetime?.Dispose(); lifetime = null; return Task.CompletedTask; }
    public async ValueTask DisposeAsync() { await Disconnect(); key.Dispose(); }
}
