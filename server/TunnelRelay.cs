using System.Net;
using System.Net.WebSockets;
using System.Security.Cryptography;

namespace RemoteDesk.Server;

internal static class TunnelRelay
{
    internal static async Task Run(HttpContext context,IConfiguration config)
    {
        if(context.Request.Path!="/relay/ws"||context.Items["LoopbackOrigin"] is not true)
        {context.Response.StatusCode=404;return;}
        if(!context.WebSockets.IsWebSocketRequest){context.Response.StatusCode=400;return;}
        using var upstream=new ClientWebSocket();
        var pin=config["RemoteDesk:GatewayRelaySha256"];
        upstream.Options.RemoteCertificateValidationCallback=(_,cert,_,_)=>!string.IsNullOrEmpty(pin)&&cert?.GetCertHashString(HashAlgorithmName.SHA256)==pin;
        using var stop=CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        try
        {
            await upstream.ConnectAsync(new Uri(config["RemoteDesk:GatewayRelayUrl"]!),stop.Token);
            using var downstream=await context.WebSockets.AcceptWebSocketAsync();
            var send=Copy(downstream,upstream,stop.Token);var receive=Copy(upstream,downstream,stop.Token);
            await Task.WhenAny(send,receive);stop.Cancel();upstream.Abort();downstream.Abort();
            try{await Task.WhenAll(send,receive);}catch(OperationCanceledException){}catch(WebSocketException){}
        }
        catch(Exception ex) when(ex is WebSocketException or OperationCanceledException or HttpRequestException)
        {if(!context.Response.HasStarted)context.Response.StatusCode=502;}
    }
    static async Task Copy(WebSocket source,WebSocket destination,CancellationToken token)
    {
        var buffer=new byte[64*1024];
        while(!token.IsCancellationRequested)
        {
            var result=await source.ReceiveAsync(new ArraySegment<byte>(buffer),token);
            if(result.MessageType==WebSocketMessageType.Close)return;
            await destination.SendAsync(new ArraySegment<byte>(buffer,0,result.Count),result.MessageType,result.EndOfMessage,token);
        }
    }
}
