using System.Diagnostics;
using System.Net;
using RemoteDesk.Infrastructure;
var root=Path.GetFullPath(args[0]);var dotnet=args[1];
var temp=Path.Combine(root,"artifacts","network-test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temp);
var c=ServiceConfiguration.Setup(temp,"192.168.7.55",false,false);var secrets=ServiceConfiguration.Load(temp).Private;
Process Launch(string dll,string urls,bool signal)
{
 var p=new ProcessStartInfo(dotnet){WorkingDirectory=temp,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};p.ArgumentList.Add(Path.Combine(root,dll));
 p.Environment["ASPNETCORE_URLS"]=urls;p.Environment["ASPNETCORE_Kestrel__Certificates__Default__Path"]=c.CertificateFile;p.Environment["ASPNETCORE_Kestrel__Certificates__Default__Password"]=secrets.CertificatePassword;
 p.Environment["RemoteDesk__RelaySecret"]=secrets.RelaySecret;
 if(signal){p.Environment["RemoteDesk__RelayUrl"]="ws://127.0.0.1:52750/relay/ws";p.Environment["RemoteDesk__RelayApiUrl"]="http://127.0.0.1:55092";p.Environment["RemoteDesk__GatewayRelayUrl"]="wss://127.0.0.1:55091/ws";p.Environment["RemoteDesk__GatewayRelaySha256"]=c.CertificateSha256;}
 var process=Process.Start(p)!;_=SaveLog(process.StandardOutput,signal?"server.log":"relay.log");_=SaveLog(process.StandardError,signal?"server.error.log":"relay.error.log");return process;
}
async Task SaveLog(StreamReader reader,string name){var contents=await reader.ReadToEndAsync();await File.WriteAllTextAsync(Path.Combine(temp,name),contents);}
using var relay=Launch("relay/bin/Release/net8.0/RemoteDesk.Relay.dll","https://127.0.0.1:55091;http://127.0.0.1:55092",false);
using var server=Launch("server/bin/Release/net8.0/RemoteDesk.Server.dll","http://127.0.0.1:52750",true);
try
{
 using var http=new HttpClient{Timeout=TimeSpan.FromSeconds(2)};bool ready=false;
 for(int i=0;i<50;i++){try{using var response=await http.GetAsync("http://127.0.0.1:52750/health");if(response.IsSuccessStatusCode){ready=true;break;}}catch(HttpRequestException){}catch(TaskCanceledException){}await Task.Delay(200);}
 if(!ready)throw new Exception("Gateway startup failed");
 foreach(var path in new[]{"/sessions","/relay/sessions","/relay/ws/other"}){using var response=await http.GetAsync("http://127.0.0.1:52750"+path);if(response.StatusCode!=HttpStatusCode.NotFound)throw new Exception("Private path exposed: "+path);}
 Console.WriteLine("PASS gateway blocks private and unrecognized routes");
 var info=new ProcessStartInfo(dotnet){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};info.ArgumentList.Add(Path.Combine(root,"tests/relay/bin/Release/net8.0/RemoteDesk.RelayTests.dll"));info.ArgumentList.Add("ws://127.0.0.1:52750/ws");info.ArgumentList.Add("http://127.0.0.1:55092");
 using var test=Process.Start(info)!;var stdout=test.StandardOutput.ReadToEndAsync();var stderr=test.StandardError.ReadToEndAsync();using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(50));await test.WaitForExitAsync(timeout.Token);string output=await stdout;Console.Write(output);if(test.ExitCode!=0||!output.Contains("Separate relay integration passed."))throw new Exception("Relay gateway integration failed: "+await stderr);
 Console.WriteLine("PASS signaling and media integration through single HTTP tunnel gateway with pinned upstream TLS");
}
finally{if(!server.HasExited)server.Kill(true);if(!relay.HasExited)relay.Kill(true);}

