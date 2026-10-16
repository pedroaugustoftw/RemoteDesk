using System.Net.WebSockets;
using RemoteDesk.Shared;
using RemoteDesk.Client.Services;
namespace RemoteDesk.Client.Streaming;
public sealed class RelayConnection : IAsyncDisposable
{
    private readonly ClientWebSocket socket=new();
    private readonly CancellationTokenSource lifetime=new();
    private readonly SemaphoreSlim gate=new(1,1);
    private readonly TaskCompletionSource attached=new(TaskCreationOptions.RunContinuationsAsynchronously);
    public event Action<Envelope>? Message;
    public async Task Connect(string url,string ticket,string sessionId,string certificatePin)
    {
        var uri=new Uri(url);if(uri.Scheme!="wss"&&!(uri.Scheme=="ws"&&uri.IsLoopback))throw new IOException("Relay exige wss:// para rede.");
        Connection.ConfigureCertificate(socket,certificatePin);
        await socket.ConnectAsync(uri,lifetime.Token);
        _=Receive();await Send(new(){Type="attach",SessionId=sessionId,Payload=ticket});
        await attached.Task.WaitAsync(TimeSpan.FromSeconds(15),lifetime.Token);_=Heartbeat();
    }
    public async Task Send(Envelope message)
    {
        var bytes=Wire.Encode(message);if(bytes.Length>Wire.MaxMessageBytes)throw new IOException("Mensagem muito grande.");
        await gate.WaitAsync(lifetime.Token);
        try{using var deadline=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);deadline.CancelAfter(TimeSpan.FromSeconds(10));await socket.SendAsync(bytes.AsMemory(),WebSocketMessageType.Text,true,deadline.Token);}finally{gate.Release();}
    }
    private async Task Receive()
    {
        try
        {
            byte[] buffer=new byte[65536];
            while(!lifetime.IsCancellationRequested)
            {
                using var content=new MemoryStream();ValueWebSocketReceiveResult result;
                do{using var deadline=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);deadline.CancelAfter(TimeSpan.FromSeconds(60));result=await socket.ReceiveAsync(buffer.AsMemory(),deadline.Token);if(result.MessageType==WebSocketMessageType.Close)return;if(result.MessageType!=WebSocketMessageType.Text||content.Length+result.Count>Wire.MaxMessageBytes)throw new IOException("Relay enviou mensagem inválida.");content.Write(buffer,0,result.Count);}while(!result.EndOfMessage);
                var message=Wire.Decode(content.ToArray())??throw new IOException("Mensagem vazia.");
                if(message.Type=="attached")attached.TrySetResult();else if(message.Type=="error"){attached.TrySetException(new IOException(message.Error));Message?.Invoke(message);}else Message?.Invoke(message);
            }
        }
        catch(Exception e){attached.TrySetException(e);}
        finally{if(!lifetime.IsCancellationRequested)Message?.Invoke(new(){Type="relay-disconnected"});socket.Abort();}
    }
    private async Task Heartbeat(){try{while(!lifetime.IsCancellationRequested){await Task.Delay(15000,lifetime.Token);await Send(new(){Type="heartbeat"});}}catch{}}
    public ValueTask DisposeAsync(){lifetime.Cancel();socket.Abort();socket.Dispose();return ValueTask.CompletedTask;}
}
