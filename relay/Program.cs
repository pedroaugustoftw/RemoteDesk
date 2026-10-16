using Microsoft.AspNetCore.HttpOverrides;
using RemoteDesk.Shared;
using RemoteDesk.Relay;
using System.Net;
using System.Security.Cryptography;
using System.Text;
var builder=WebApplication.CreateBuilder(args);
var secret=builder.Configuration["RemoteDesk:RelaySecret"];
if(secret is null || secret.Length<32) throw new InvalidOperationException("RemoteDesk:RelaySecret must have at least 32 characters.");
builder.Services.AddSingleton<MediaRelay>();
builder.Services.AddHostedService(p=>p.GetRequiredService<MediaRelay>());
var app=builder.Build();
var forwarded=new ForwardedHeadersOptions {ForwardedHeaders=ForwardedHeaders.XForwardedFor|ForwardedHeaders.XForwardedProto};
foreach(var ip in builder.Configuration.GetSection("RemoteDesk:KnownProxies").Get<string[]>()??[]) forwarded.KnownProxies.Add(IPAddress.Parse(ip));
app.UseForwardedHeaders(forwarded);
app.Use(async (ctx,next)=>
{
    if(!ctx.Request.IsHttps && (ctx.Connection.RemoteIpAddress is null || !IPAddress.IsLoopback(ctx.Connection.RemoteIpAddress))) {ctx.Response.StatusCode=403;return;}
    await next();
});
app.UseWebSockets();
bool Authorized(HttpContext ctx)
{
    var header=ctx.Request.Headers.Authorization.ToString();
    return CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(header)),SHA256.HashData(Encoding.UTF8.GetBytes("Bearer "+secret)));
}
app.MapGet("/health",()=>Results.Ok(new {application="RemoteDesk Relay",status="ok"}));
app.MapPost("/sessions",(HttpContext ctx,RelayRegistration registration,MediaRelay relay)=>
{
    if(!Authorized(ctx)) return Results.Unauthorized();
    if(registration.SessionId is not {Length: >0 and <=64} || registration.Permissions is null || !registration.Permissions.View || !registration.Permissions.IsSubsetOf(new(true,true,true))) return Results.BadRequest();
    var tickets=relay.Create(registration); return tickets is null ? Results.Conflict() : Results.Ok(tickets);
});
app.MapDelete("/sessions/{id}",(string id,HttpContext ctx,MediaRelay relay)=>
{
    if(!Authorized(ctx)) return Results.Unauthorized();
    relay.Revoke(id); return Results.NoContent();
});
app.MapPut("/sessions/{id}",(string id,HttpContext ctx,MediaRelay relay)=>
{
    if(!Authorized(ctx)) return Results.Unauthorized();
    return relay.Renew(id) ? Results.NoContent() : Results.NotFound();
});
app.Map("/ws",async (HttpContext ctx,MediaRelay relay)=>
{
    if(!ctx.Request.IsHttps && (ctx.Connection.RemoteIpAddress is null || !IPAddress.IsLoopback(ctx.Connection.RemoteIpAddress))) {ctx.Response.StatusCode=403;return;}
    if(!ctx.WebSockets.IsWebSocketRequest) {ctx.Response.StatusCode=400;return;}
    using var ws=await ctx.WebSockets.AcceptWebSocketAsync(); await relay.Run(ws,ctx.RequestAborted);
});
await app.RunAsync();
