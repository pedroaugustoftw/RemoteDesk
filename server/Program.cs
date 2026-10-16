using RemoteDesk.Server;
using Microsoft.AspNetCore.HttpOverrides;
using System.Net;
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<Broker>();
builder.Services.AddHostedService(provider => provider.GetRequiredService<Broker>());
var app = builder.Build();
var forwarded = new ForwardedHeadersOptions { ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto };
foreach(var address in builder.Configuration.GetSection("RemoteDesk:KnownProxies").Get<string[]>() ?? [])
    forwarded.KnownProxies.Add(IPAddress.Parse(address));
app.Use(async (context,next)=>{context.Items["LoopbackOrigin"]=context.Connection.RemoteIpAddress is {} ip&&IPAddress.IsLoopback(ip);await next();});
app.UseForwardedHeaders(forwarded);
app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(20) });
app.MapGet("/health", () => Results.Ok(new { application = "RemoteDesk", status = "ok" }));
app.Map("/ws", async (HttpContext context, Broker broker) =>
{
    // Plaintext is permitted only on loopback for development. Configure TLS on the VPS.
    if (!context.Request.IsHttps && context.Items["LoopbackOrigin"] is not true)
    { context.Response.StatusCode = 403; return; }
    if (!context.WebSockets.IsWebSocketRequest) { context.Response.StatusCode = 400; return; }
    using var socket = await context.WebSockets.AcceptWebSocketAsync();
    await broker.Run(socket, context.RequestAborted);
});
if(!string.IsNullOrEmpty(builder.Configuration["RemoteDesk:GatewayRelayUrl"]))
    app.Map("/relay/ws", (HttpContext context)=>TunnelRelay.Run(context,builder.Configuration));
await app.RunAsync();
