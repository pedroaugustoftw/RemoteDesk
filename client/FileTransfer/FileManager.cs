using System.IO;
using System.Security.Cryptography;
using System.Collections.Concurrent;
using RemoteDesk.Shared;
using Microsoft.Win32;
using System.Windows;
namespace RemoteDesk.Client.FileTransfer;
public sealed class FileManager(Func<Envelope,Task> send, Action<string> progress, Func<string,long,string?>? chooseDestination = null)
{
    private sealed record Incoming(FileStream Stream, string Temp, string Destination, long Size, string Hash,bool Replace=true);
    private readonly Dictionary<string,Incoming> incoming = [];
    private readonly ConcurrentDictionary<string,TaskCompletionSource<bool>> accepted = new();
    private readonly ConcurrentDictionary<string,CancellationTokenSource> outgoing = new();
    private string? session;
    private bool selectedDestinationAllowed;
    private CancellationTokenSource lifetime=new();
    private readonly ConcurrentDictionary<string,(string Path,bool Replace)> requestedDownloads=new();
    private readonly ConcurrentDictionary<string,TaskCompletionSource<bool>> finished=new();
    public FileBrowser Browser {get;private set;}=new(_=>Task.FromException(new IOException("Sessão encerrada.")),(_,_)=>{});
    public event Action<string>? ProgressChanged;
    void Report(string message){progress(message);ProgressChanged?.Invoke(message);}

    public void Start(string sessionId,bool allowSelectedDestination=false)
    {
        lifetime.Dispose();lifetime=new();session=sessionId;selectedDestinationAllowed=allowSelectedDestination;
        Browser=new(f=>send(new(){Type="file",SessionId=sessionId,File=f}),(path,id)=>{if(session==sessionId)_=SendPath(path,id);});
    }
    public async Task Download(string remotePath,string destination,bool overwrite=false)
    {
        if(session is null)throw new IOException("Sessão encerrada.");
        string id=Guid.NewGuid().ToString("N");requestedDownloads[id]=(destination,overwrite);var done=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);finished[id]=done;
        try{await Send(new("pull",id,Path:remotePath));Report("Solicitando arquivo remoto…");if(!await done.Task.WaitAsync(TimeSpan.FromMinutes(10)))throw new IOException("Transferência recusada ou cancelada.");}
        finally{requestedDownloads.TryRemove(id,out _);finished.TryRemove(id,out _);}
    }
    private Task Send(FileMessage file) => session is null ? Task.CompletedTask : send(new Envelope {Type="file",SessionId=session,File=file});
    public async Task SendFile(Window owner)
    {
        if(session is null)return;
        var dialog=new OpenFileDialog {Title="RemoteDesk — Enviar arquivo"}; if(dialog.ShowDialog(owner)!=true)return;
        await SendPath(dialog.FileName);
    }
    public async Task<bool> SendPath(string sourcePath,string? transferId=null,string? remoteDestination=null,bool overwrite=false)
    {
        string? transferSession=session;if(transferSession is null)return false;
        Task SendOutgoing(FileMessage f)=>send(new(){Type="file",SessionId=transferSession,File=f});
        string id=transferId??Guid.NewGuid().ToString("N"); var cancel=new CancellationTokenSource(); outgoing[id]=cancel;
        var ready=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously); accepted[id]=ready;var completed=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);finished[id]=completed;
        try
        {
            using var source=new FileStream(sourcePath,FileMode.Open,FileAccess.Read,FileShare.Read,65536,true);
            if(source.Length>2L*1024*1024*1024)throw new IOException("Limite por arquivo: 2 GiB.");
            Report("Calculando integridade…"); var hash=Convert.ToHexString(await SHA256.HashDataAsync(source,cancel.Token));source.Position=0;
            cancel.Token.ThrowIfCancellationRequested();await SendOutgoing(new("offer",id,Path.GetFileName(sourcePath),source.Length,hash,Path:remoteDestination,Replace:overwrite));Report("Aguardando confirmação do destino…");
            if(!await ready.Task.WaitAsync(TimeSpan.FromMinutes(2),cancel.Token))return false;
            byte[] buffer=new byte[65536];long offset=0;int count;
            while((count=await source.ReadAsync(buffer,cancel.Token))>0)
            {
                cancel.Token.ThrowIfCancellationRequested();
                await SendOutgoing(new("chunk",id,Chunk:Convert.ToBase64String(buffer,0,count),Offset:offset));offset+=count;
                // Bound bursts so files leave room for screen/input below the relay's message limit.
                await Task.Delay(20,cancel.Token);
                Report($"Enviando {Path.GetFileName(sourcePath)}: {offset*100/Math.Max(1,source.Length)}%");
            }
            cancel.Token.ThrowIfCancellationRequested();await SendOutgoing(new("complete",id));Report("Enviado; aguardando verificação SHA-256 do destino…");if(!await completed.Task.WaitAsync(TimeSpan.FromMinutes(10),cancel.Token))throw new IOException("Destino não confirmou o arquivo.");return true;
        }
        catch(Exception e) {Report(e is OperationCanceledException?"Transferência cancelada":e.Message);try{await SendOutgoing(new("cancel",id));}catch{} return false;}
        finally {finished.TryRemove(id,out _);accepted.TryRemove(id,out _);outgoing.TryRemove(id,out _);cancel.Dispose();}
    }
    // Called serially by the UI dispatcher. File names never select a destination automatically.
    public async Task Receive(FileMessage f, Window? owner)
    {
        if(session is null)return;
        if(f.Kind is "browse" or "listing" or "pull" or "mkdir"){await Browser.Receive(f);return;}
        switch(f.Kind)
        {
            case "offer":
                if(incoming.Count>=2||f.Size<0||f.Size>2L*1024*1024*1024||f.Hash?.Length!=64||!f.Hash.All(Uri.IsHexDigit)||string.IsNullOrWhiteSpace(f.Name)) {await Send(new("reject",f.TransferId));return;}
                try
                {
                var currentSession=session;string? destination;bool replaceExisting=true;
                if(f.Path is not null&&!f.Replace){try{if(File.Exists(FileBrowser.Normalize(f.Path))){await Send(new("reject",f.TransferId));Report("Arquivo já existe no destino. Habilite Substituir para sobrescrever.");return;}}catch(IOException){await Send(new("reject",f.TransferId));return;}}
                if(requestedDownloads.TryRemove(f.TransferId,out var requested)){destination=requested.Path;replaceExisting=requested.Replace;}
                else if(selectedDestinationAllowed&&f.Path is not null)
                {
                    destination=FileBrowser.Normalize(f.Path);replaceExisting=f.Replace;
                    if(!Path.GetFileName(destination).Equals(Path.GetFileName(f.Name),StringComparison.OrdinalIgnoreCase))throw new IOException("Nome do arquivo não corresponde ao destino.");
                }
                else if(chooseDestination!=null)destination=chooseDestination(Path.GetFileName(f.Name),f.Size);
                else {string suggested="";try{if(f.Path is not null)suggested=Path.GetDirectoryName(FileBrowser.Normalize(f.Path))??"";}catch{}
                    var d=new SaveFileDialog {Title=$"RemoteDesk — Receber {f.Size:N0} bytes",FileName=Path.GetFileName(f.Name),InitialDirectory=suggested,OverwritePrompt=true};destination=d.ShowDialog(owner)==true?d.FileName:null;}
                if(currentSession!=session)return;
                if(destination is null){await Send(new("reject",f.TransferId));return;}
                if(incoming.ContainsKey(f.TransferId)){await Send(new("reject",f.TransferId));return;}
                var temp=destination+".remotedesk-"+Guid.NewGuid().ToString("N")+".part";
                incoming.Add(f.TransferId,new(new FileStream(temp,FileMode.CreateNew,FileAccess.ReadWrite,FileShare.None),temp,destination,f.Size,f.Hash,replaceExisting));
                await Send(new("accept",f.TransferId));Report("Recebendo "+Path.GetFileName(destination));break;
                }
                catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or ArgumentException)
                {if(finished.TryGetValue(f.TransferId,out var rejectedOffer))rejectedOffer.TrySetResult(false);await Send(new("reject",f.TransferId));Report(ex.Message);break;}
            case "accept": if(accepted.TryGetValue(f.TransferId,out var a))a.TrySetResult(true);break;
            case "reject": if(finished.TryGetValue(f.TransferId,out var rejected))rejected.TrySetResult(false);requestedDownloads.TryRemove(f.TransferId,out _);if(accepted.TryGetValue(f.TransferId,out var r))r.TrySetResult(false);Report("Destino recusou o arquivo.");break;
            case "chunk":
                if(!incoming.TryGetValue(f.TransferId,out var item))return;
                byte[] bytes;try{bytes=Convert.FromBase64String(f.Chunk??"");}catch(FormatException){Drop(f.TransferId);await Send(new("cancel",f.TransferId));throw new IOException("Bloco de arquivo inválido.");}
                if(bytes.Length>65536||f.Offset!=item.Stream.Position||item.Stream.Position+bytes.Length>item.Size) {Drop(f.TransferId);await Send(new("cancel",f.TransferId));throw new IOException("Bloco de arquivo inválido.");}
                item.Stream.Write(bytes);Report($"Recebendo: {item.Stream.Position*100/Math.Max(1,item.Size)}%");break;
            case "complete":
                if(!incoming.Remove(f.TransferId,out var done))return;
                try
                {
                    if(done.Stream.Length!=done.Size)throw new IOException("Arquivo incompleto.");
                    done.Stream.Flush(true);done.Stream.Position=0;var receivingToken=lifetime.Token;
                    if(!CryptographicOperations.FixedTimeEquals(await SHA256.HashDataAsync(done.Stream,receivingToken),Convert.FromHexString(done.Hash)))throw new IOException("Falha na integridade SHA-256.");
                    receivingToken.ThrowIfCancellationRequested();done.Stream.Dispose();File.Move(done.Temp,done.Destination,done.Replace);Report("Arquivo recebido e SHA-256 verificado.");if(finished.TryGetValue(f.TransferId,out var received))received.TrySetResult(true);await Send(new("verified",f.TransferId));
                }
                catch {if(finished.TryGetValue(f.TransferId,out var failed))failed.TrySetResult(false);done.Stream.Dispose();File.Delete(done.Temp);await Send(new("cancel",f.TransferId));throw;}break;
            case "verified":if(finished.TryGetValue(f.TransferId,out var verified))verified.TrySetResult(true);Report("Destino confirmou integridade SHA-256.");break;
            case "cancel":if(finished.TryGetValue(f.TransferId,out var canceled))canceled.TrySetResult(false);requestedDownloads.TryRemove(f.TransferId,out _);Drop(f.TransferId);if(outgoing.TryGetValue(f.TransferId,out var c))c.Cancel();Report("Transferência cancelada.");break;
        }
    }
    private void Drop(string id) {if(finished.TryGetValue(id,out var done))done.TrySetResult(false);if(incoming.Remove(id,out var x)){x.Stream.Dispose();File.Delete(x.Temp);}}
    public async Task Cancel()
    {
        foreach(var id in requestedDownloads.Keys){if(finished.TryGetValue(id,out var request))request.TrySetCanceled();try{await Send(new("cancel",id));}catch{}requestedDownloads.TryRemove(id,out _);}
        foreach(var pair in outgoing){pair.Value.Cancel();try{await Send(new("cancel",pair.Key));}catch{}}
        foreach(var id in incoming.Keys.ToArray()){Drop(id);try{await Send(new("cancel",id));}catch{}}
    }
    public void Stop() {lifetime.Cancel();foreach(var f in finished.Values)f.TrySetCanceled();finished.Clear();Browser.Stop();requestedDownloads.Clear();session=null;foreach(var c in outgoing.Values)c.Cancel();foreach(var id in incoming.Keys.ToArray())Drop(id);foreach(var a in accepted.Values)a.TrySetCanceled();}
}
