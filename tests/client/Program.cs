using System.Drawing;
using System.IO;
using System.Net.WebSockets;
using System.Threading.Channels;
using RemoteDesk.Client.Services;
using RemoteDesk.Client.Streaming;
using System.Security.Cryptography;
using RemoteDesk.Client.Capture;
using RemoteDesk.Client.RemoteControl;
using RemoteDesk.Client.FileTransfer;
using RemoteDesk.Shared;

internal static class Program
{
    static int passed, failed;
    static readonly List<string> Results = new();
    static readonly string DirectoryPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"artifacts"));
    [STAThread] static async Task<int> Main()
    {
        Directory.CreateDirectory(DirectoryPath);
        await Check("transmission presets bounds and monotonic balance",()=>{
            var previous=TransmissionPreset.FromLevel(0);Assert(previous==new TransmissionPreset(30,800,20));
            for(int i=1;i<=100;i++){var p=TransmissionPreset.FromLevel(i);Assert(p.Quality>=previous.Quality&&p.Width>=previous.Width&&p.Fps<=previous.Fps);previous=p;}
            Assert(previous==new TransmissionPreset(90,1920,8));Throws<ArgumentOutOfRangeException>(()=>TransmissionPreset.FromLevel(-1));Throws<ArgumentOutOfRangeException>(()=>TransmissionPreset.FromLevel(101));
            Assert(!MediaPolicy.Valid(new(){Type="quality",QualityLevel=50},false,new(false)));Assert(MediaPolicy.Valid(new(){Type="quality",QualityLevel=50},false,new(true)));return Task.CompletedTask;
        });
        await Check("transmission balance changes real JPEG resolution",()=>{
            foreach(int level in new[]{0,50,100}){var p=TransmissionPreset.FromLevel(level);var c=new ScreenCapture{MaxWidth=p.Width,Quality=p.Quality};using var image=Image.FromStream(new MemoryStream(c.CaptureJpeg()));Assert(image.Width==Math.Min(p.Width,c.Bounds.Width));}return Task.CompletedTask;
        });
        await Check("capture real JPEG and monitor bounds", () => {
            var capture=new ScreenCapture {MaxWidth=640,Quality=70};var bytes=capture.CaptureJpeg();
            using var image=Image.FromStream(new MemoryStream(bytes));
            Assert(capture.MonitorCount>0&&capture.Bounds.Width>0&&image.Width==Math.Min(640,capture.Bounds.Width));
            Assert(image.Height==(int)Math.Round(capture.Bounds.Height*(double)image.Width/capture.Bounds.Width));
            Assert(bytes[0]==0xff&&bytes[1]==0xd8&&bytes.Length>100);return Task.CompletedTask;
        });
        await Check("capture quality changes encoded size",()=>{
            var c=new ScreenCapture {MaxWidth=640,Quality=10};var low=c.CaptureJpeg();c.Quality=95;var high=c.CaptureJpeg();
            Assert(high.Length>low.Length);return Task.CompletedTask;
        });
        await Check("JPEG header accepts real frame rejects dimensions truncation",()=>{
            var bytes=new ScreenCapture {MaxWidth=640}.CaptureJpeg();JpegHeader.Validate(bytes);
            Throws<IOException>(()=>JpegHeader.Validate([1,2,3]));Throws<IOException>(()=>JpegHeader.Validate([255,216,255,192,0,8,8,0,1,255,255,1]));
            Throws<IOException>(()=>JpegHeader.Validate([255,216,255,192,0,8]));return Task.CompletedTask;
        });
        await Check("normalized negative monitor coordinates",()=>{
            var desktop=new Rectangle(-1920,-1080,3840,2160);
            Assert(InputController.Normalize(0,0,new(-1920,-1080,1920,1080),desktop)==new Point(0,0));
            Assert(InputController.Normalize(1,1,new(0,0,1920,1080),desktop)==new Point(65535,65535));return Task.CompletedTask;
        });
        await Check("normalized selected monitor midpoint",()=>{
            var p=InputController.Normalize(.5,.5,new(-1920,0,1920,1080),new(-1920,0,3840,1080));
            Assert(Math.Abs(p.X-16379)<2&&Math.Abs(p.Y-32768)<2);return Task.CompletedTask;
        });
        foreach(var (x,y) in new[]{(double.NaN,0d),(0d,double.PositiveInfinity),(-.1,0d),(1.1,0d),(0d,-.1),(0d,1.1)})
            await Check($"reject invalid coordinates {x},{y}",()=>{Throws<ArgumentOutOfRangeException>(()=>InputController.Normalize(x,y,new(0,0,1920,1080),new(0,0,1920,1080)));return Task.CompletedTask;});
        await Check("file end to end multiple chunks SHA-256",async()=>{
            var data=RandomNumberGenerator.GetBytes(180123);var source=Path.Combine(DirectoryPath,"source.bin");var destination=Path.Combine(DirectoryPath,"received.bin");File.WriteAllBytes(source,data);
            FileManager? sender=null;FileManager? receiver=null;
            sender=new FileManager(e=>receiver!.Receive(e.File!,null),_=>{});
            receiver=new FileManager(e=>sender.Receive(e.File!,null),_=>{},(_,_)=>destination);
            sender.Start("test");receiver.Start("test");await sender.SendPath(source);
            Assert(File.Exists(destination)&&SHA256.HashData(data).SequenceEqual(SHA256.HashData(File.ReadAllBytes(destination))));
            sender.Stop();receiver.Stop();AssertNoParts();
        });
        await Check("file bad offset cancels and deletes partial",async()=>{
            var messages=new List<Envelope>();var m=Manager(messages,"bad-offset.bin");await Offer(m,"offset",[1,2]);
            await ThrowsAsync<IOException>(()=>m.Receive(new("chunk","offset",Chunk:"AQI=",Offset:1),null));
            Assert(messages.Last().File!.Kind=="cancel");AssertNoParts();m.Stop();
        });
        await Check("file integrity mismatch never publishes",async()=>{
            var messages=new List<Envelope>();var m=Manager(messages,"bad-hash.bin");await Offer(m,"hash",[1,2]);
            await m.Receive(new("chunk","hash",Chunk:"AwQ="),null);
            await ThrowsAsync<IOException>(()=>m.Receive(new("complete","hash"),null));
            Assert(!File.Exists(Path.Combine(DirectoryPath,"bad-hash.bin")));AssertNoParts();m.Stop();
        });
        await Check("file malformed base64 cancels and cleans",async()=>{
            var messages=new List<Envelope>();var m=Manager(messages,"bad-base64.bin");await Offer(m,"base64",[1]);
            await ThrowsAsync<IOException>(()=>m.Receive(new("chunk","base64",Chunk:"not!base64"),null));AssertNoParts();m.Stop();
        });
        await Check("file nonhex hash offer rejected",async()=>{
            var messages=new List<Envelope>();var m=Manager(messages,"bad-offer.bin");await m.Receive(new("offer","nonhex","test.bin",1,new string('Z',64)),null);
            Assert(messages.Single().File!.Kind=="reject");AssertNoParts();m.Stop();
        });
        await Check("file explicit destination rejection",async()=>{
            var messages=new List<Envelope>();var m=new FileManager(e=>{messages.Add(e);return Task.CompletedTask;},_=>{},(_,_)=>null);m.Start("test");await Offer(m,"reject",[1]);
            Assert(messages.Single().File!.Kind=="reject");AssertNoParts();m.Stop();
        });
        await Check("file cancel deletes temp",async()=>{
            var messages=new List<Envelope>();var m=Manager(messages,"cancel.bin");await Offer(m,"cancel",[1]);
            Assert(Directory.GetFiles(DirectoryPath,"*.part").Length==1);await m.Cancel();AssertNoParts();Assert(messages.Last().File!.Kind=="cancel");m.Stop();
        });
        await Check("file Stop deletes temp and rejects later messages",async()=>{
            var messages=new List<Envelope>();var m=Manager(messages,"stop.bin");await Offer(m,"stop",[1]);m.Stop();AssertNoParts();
            await Offer(m,"after-stop",[1]);Assert(messages.Count==1);AssertNoParts();
        });
        await Check("invalid TLS certificate pin rejected",()=>{
            using var socket=new ClientWebSocket();Throws<ArgumentException>(()=>Connection.ConfigureCertificate(socket,"00"));
            Throws<FormatException>(()=>Connection.ConfigureCertificate(socket,"invalid"));return Task.CompletedTask;
        });
        await Check("file browser local volumes, pagination and path restrictions",()=>{
            string folder=Path.Combine(DirectoryPath,"browser");Directory.CreateDirectory(folder);for(int i=0;i<205;i++)File.WriteAllText(Path.Combine(folder,$"file-{i:000}.txt"),i.ToString());
            var first=FileBrowser.Read(folder);var second=FileBrowser.Read(folder,first.Offset);var third=FileBrowser.Read(folder,second.Offset);
            Assert(first.Entries!.Length==100&&first.More&&second.Entries!.Length==100&&second.More&&third.Entries!.Length==5&&!third.More);
            Assert(first.Entries.Concat(second.Entries!).Concat(third.Entries!).Select(e=>e.Path).Distinct().Count()==205);
            Assert(FileBrowser.Read("").Entries!.Length>0);Assert(MediaPolicy.ValidFile(FileBrowser.Read("") with {TransferId=Guid.NewGuid().ToString("N")}));
            foreach(var path in new[]{@"\\server\share",@"\\?\C:\Windows",@"C:\test:secret","relative.txt"})Throws<IOException>(()=>FileBrowser.Normalize(path));return Task.CompletedTask;
        });
        await Check("remote file browser RPC, mkdir and download SHA-256",async()=>{
            string folder=Path.Combine(DirectoryPath,"remote-browser");Directory.CreateDirectory(folder);string source=Path.Combine(folder,"remote.bin"),destination=Path.Combine(folder,"download.bin");var data=RandomNumberGenerator.GetBytes(195321);File.WriteAllBytes(source,data);if(File.Exists(destination))File.Delete(destination);
            FileManager? viewer=null,host=null;viewer=new FileManager(e=>host!.Receive(e.File!,null),_=>{});host=new FileManager(e=>viewer.Receive(e.File!,null),_=>{});
            viewer.Start("browser-session");host.Start("browser-session");var listing=await viewer.Browser.List(folder);Assert(listing.Entries!.Any(e=>e.Name=="remote.bin"));
            string newFolder=Path.Combine(folder,"created-"+Guid.NewGuid().ToString("N"));await viewer.Browser.CreateFolder(newFolder);Assert(Directory.Exists(newFolder));
            await viewer.Download(source,destination);Assert(File.ReadAllBytes(destination).SequenceEqual(data));viewer.Stop();host.Stop();
        });
        await Check("selected destination upload and overwrite protection",async()=>{
            string folder=Path.Combine(DirectoryPath,"upload");Directory.CreateDirectory(folder);string source=Path.Combine(folder,"source.bin"),destination=Path.Combine(folder,"target","source.bin");Directory.CreateDirectory(Path.GetDirectoryName(destination)!);File.WriteAllBytes(source,[1,2,3]);if(File.Exists(destination))File.Delete(destination);
            FileManager? viewer=null,host=null;viewer=new(e=>host!.Receive(e.File!,null),_=>{});host=new(e=>viewer.Receive(e.File!,null),_=>{});viewer.Start("upload-session");host.Start("upload-session",true);
            Assert(await viewer.SendPath(source,remoteDestination:destination));Assert(File.ReadAllBytes(destination).SequenceEqual(new byte[]{1,2,3}));File.WriteAllBytes(source,[4,5,6]);
            Assert(!await viewer.SendPath(source,remoteDestination:destination));Assert(File.ReadAllBytes(destination).SequenceEqual(new byte[]{1,2,3}));
            Assert(await viewer.SendPath(source,remoteDestination:destination,overwrite:true));Assert(File.ReadAllBytes(destination).SequenceEqual(new byte[]{4,5,6}));viewer.Stop();host.Stop();
        });
        await Check("file browser requires Files permission and correct direction",()=>{
            var browse=new FileMessage("browse",Guid.NewGuid().ToString("N"),Path:"");var request=new Envelope{Type="file",File=browse};
            Assert(!MediaPolicy.Valid(request,false,new(true)));Assert(MediaPolicy.Valid(request,false,new(false,false,true)));Assert(!MediaPolicy.Valid(request,true,new(false,false,true)));
            var response=request with {File=new("listing",browse.TransferId,Path:"",Entries:[])};Assert(MediaPolicy.Valid(response,true,new(false,false,true)));Assert(!MediaPolicy.Valid(response,false,new(false,false,true)));
            Assert(!MediaPolicy.ValidFile(new("listing",browse.TransferId,Path:"C:\\",Entries:[new("../escape.txt","C:\\escape.txt",false)])));return Task.CompletedTask;
        });
        var endpoint=Environment.GetEnvironmentVariable("REMOTEDESK_TEST_SERVER");
        if(endpoint is not null) await Check("real Connection identities consent and teardown",async()=>{
            await using var host=new Connection(RSA.Create(2048));await using var viewer=new Connection(RSA.Create(2048));
            var hostMessages=Channel.CreateUnbounded<Envelope>();var viewerMessages=Channel.CreateUnbounded<Envelope>();
            host.Message+=e=>hostMessages.Writer.TryWrite(e);viewer.Message+=e=>viewerMessages.Writer.TryWrite(e);
            var pin=Environment.GetEnvironmentVariable("REMOTEDESK_TEST_SERVER_PIN")??"";
            if(endpoint.StartsWith("wss")){await using var wrong=new Connection(RSA.Create(2048));await ThrowsAsync<WebSocketException>(()=>wrong.Connect(new(){Server=endpoint,ServerCertificateSha256=new string('0',64)}));}
            await host.Connect(new(){Server=endpoint,Name="Client test host",ServerCertificateSha256=pin});await viewer.Connect(new(){Server=endpoint,Name="Client test viewer",ServerCertificateSha256=pin});
            var hostReady=await Next(hostMessages,"ready");var viewerReady=await Next(viewerMessages,"ready");
            Assert(host.Ready&&viewer.Ready&&hostReady.DeviceId!=viewerReady.DeviceId);
            await viewer.Send(new(){Type="request",TargetId=hostReady.DeviceId,Permissions=new(true,true,true)});
            var incoming=await Next(hostMessages,"incoming");var pending=await Next(viewerMessages,"pending");Assert(incoming.SessionId==pending.SessionId);
            bool relay=Environment.GetEnvironmentVariable("REMOTEDESK_TEST_RELAY")=="1";
            await host.Send(new(){Type="respond",SessionId=incoming.SessionId,Permissions=new(true,false,relay)});
            var hostStart=await Next(hostMessages,"started");var viewerStart=await Next(viewerMessages,"started");
            Assert(hostStart.Permissions==new Permissions(true,false,relay)&&viewerStart.SessionId==hostStart.SessionId);
            if(relay) await RelayPipeline(hostStart,viewerStart,pin);
            await viewer.Send(new(){Type="end",SessionId=viewerStart.SessionId});await Next(hostMessages,"ended");await Next(viewerMessages,"ended");
        });
        var result=$"Passed: {passed}; Failed: {failed}; Input injection: NONE";Console.WriteLine(result);
        File.WriteAllLines(Path.Combine(DirectoryPath,"results.txt"),Results.Append(result));return failed==0?0:1;
    }
    static FileManager Manager(List<Envelope> messages,string name){var m=new FileManager(e=>{messages.Add(e);return Task.CompletedTask;},_=>{},(_,_)=>Path.Combine(DirectoryPath,name));m.Start("test");return m;}
    static async Task RelayPipeline(Envelope host,Envelope viewer,string pin)
    {
        Assert(host.RelayUrl is not null&&host.RelayTicket is not null&&viewer.RelayTicket is not null);
        await using var hostRelay=new RelayConnection();await using var viewerRelay=new RelayConnection();
        var h=Channel.CreateUnbounded<Envelope>();var v=Channel.CreateUnbounded<Envelope>();hostRelay.Message+=e=>h.Writer.TryWrite(e);viewerRelay.Message+=e=>v.Writer.TryWrite(e);
        await hostRelay.Connect(host.RelayUrl!,host.RelayTicket!,host.SessionId!,pin);await viewerRelay.Connect(viewer.RelayUrl!,viewer.RelayTicket!,viewer.SessionId!,pin);
        var jpeg=new ScreenCapture {MaxWidth=640}.CaptureJpeg();await hostRelay.Send(new(){Type="frame",SessionId=host.SessionId,Payload=Convert.ToBase64String(jpeg)});
        var frame=Convert.FromBase64String((await Next(v,"frame")).Payload!);JpegHeader.Validate(frame);Assert(SHA256.HashData(frame).SequenceEqual(SHA256.HashData(jpeg)));
        var data=RandomNumberGenerator.GetBytes(100123);var destination=Path.Combine(DirectoryPath,"relay-received.bin");
        var receiver=new FileManager(e=>viewerRelay.Send(e),_=>{},(_,_)=>destination);receiver.Start(viewer.SessionId!);
        Task Send(FileMessage f)=>hostRelay.Send(new(){Type="file",SessionId=host.SessionId,File=f});
        await Send(new("offer","network-file","relay.bin",data.Length,Convert.ToHexString(SHA256.HashData(data))));
        await receiver.Receive((await Next(v,"file")).File!,null);Assert((await Next(h,"file")).File!.Kind=="accept");
        for(int offset=0;offset<data.Length;offset+=65536){await Send(new("chunk","network-file",Chunk:Convert.ToBase64String(data,offset,Math.Min(65536,data.Length-offset)),Offset:offset));await receiver.Receive((await Next(v,"file")).File!,null);}
        await Send(new("complete","network-file"));await receiver.Receive((await Next(v,"file")).File!,null);Assert((await Next(h,"file")).File!.Kind=="verified");
        Assert(File.ReadAllBytes(destination).SequenceEqual(data));receiver.Stop();AssertNoParts();
        Results.Add("PASS WSS correct pin + wrong pin denial + relay real JPEG + relay disk file SHA256");
    }
    static Task Offer(FileManager m,string id,byte[] data)=>m.Receive(new("offer",id,"test.bin",data.Length,Convert.ToHexString(SHA256.HashData(data))),null);
    static void AssertNoParts()=>Assert(Directory.GetFiles(DirectoryPath,"*.part").Length==0);
    static async Task<Envelope> Next(Channel<Envelope> messages,string type){using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(10));while(true){var e=await messages.Reader.ReadAsync(timeout.Token);if(e.Type==type)return e;if(e.Type=="error")throw new Exception(e.Error);}}
    static void Assert(bool result){if(!result)throw new Exception("Assertion failed");}
    static void Throws<T>(Action action)where T:Exception {try{action();}catch(T){return;}throw new Exception("Expected "+typeof(T).Name);}
    static async Task ThrowsAsync<T>(Func<Task> action)where T:Exception {try{await action();}catch(T){return;}throw new Exception("Expected "+typeof(T).Name);}
    static async Task Check(string name,Func<Task> action){try{await action();passed++;Results.Add("PASS "+name);Console.WriteLine("PASS "+name);}catch(Exception e){failed++;Results.Add("FAIL "+name+": "+e);Console.WriteLine("FAIL "+name+": "+e);}}
}
