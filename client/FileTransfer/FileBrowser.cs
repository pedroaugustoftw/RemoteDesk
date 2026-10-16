using System.Collections.Concurrent;
using RemoteDesk.Shared;

namespace RemoteDesk.Client.FileTransfer;

public sealed class FileBrowser(Func<FileMessage,Task> send,Action<string,string> pull)
{
    readonly ConcurrentDictionary<string,TaskCompletionSource<FileMessage>> pending=new();
    public async Task<FileMessage> List(string path,long offset=0)=>await Request(new("browse",Guid.NewGuid().ToString("N"),Offset:offset,Path:path));
    public async Task<FileMessage> CreateFolder(string path)=>await Request(new("mkdir",Guid.NewGuid().ToString("N"),Path:path));
    async Task<FileMessage> Request(FileMessage message)
    {
        var result=new TaskCompletionSource<FileMessage>(TaskCreationOptions.RunContinuationsAsynchronously);pending[message.TransferId]=result;
        try{await send(message);var response=await result.Task.WaitAsync(TimeSpan.FromSeconds(15));if(response.Error is not null)throw new IOException(response.Error);return response;}
        catch(TimeoutException){throw new IOException("Sem resposta do explorador remoto. Atualize os dois aplicativos e os componentes do servidor.");}
        finally{pending.TryRemove(message.TransferId,out _);}
    }
    public void Stop(){foreach(var p in pending.Values)p.TrySetCanceled();pending.Clear();}
    public async Task Receive(FileMessage message)
    {
        if(message.Kind=="listing"){if(pending.TryGetValue(message.TransferId,out var reply))reply.TrySetResult(message);return;}
        if(message.Kind=="pull")
        {
            try{pull(Normalize(message.Path!),message.TransferId);}
            catch(Exception){await send(new("reject",message.TransferId));}
            return;
        }
        try
        {
            var response=await Task.Run(()=>{
                if(message.Kind=="mkdir")
                {
                    string path=Normalize(message.Path!);if(System.IO.Directory.Exists(path)||File.Exists(path))throw new IOException("Já existe um item com esse nome.");
                    System.IO.Directory.CreateDirectory(path);return Read(System.IO.Directory.GetParent(path)!.FullName);
                }
                return Read(message.Path??"",message.Offset);
            });
            await send(response with {TransferId=message.TransferId});
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException)
        {await send(new("listing",message.TransferId,Path:message.Path??"",Entries:[],Error:ex.Message[..Math.Min(ex.Message.Length,500)]));}
    }
    public static string Normalize(string path)
    {
        if(path.Length>2048||path.Length<3||!char.IsAsciiLetter(path[0])||path[1]!=':'||path[2]!='\\'||path[2..].Contains(':')||path.Contains('\0'))throw new IOException("Use um caminho local, como C:\\Users. Caminhos de rede e de dispositivo não são aceitos.");
        var full=System.IO.Path.GetFullPath(path);
        if(new DriveInfo(System.IO.Path.GetPathRoot(full)!).DriveType==DriveType.Network)throw new IOException("Unidades de rede não são aceitas nesta sessão.");
        for(string? part=full;part is not null;part=System.IO.Directory.GetParent(part)?.FullName)
            if((System.IO.Directory.Exists(part)||File.Exists(part))&&(File.GetAttributes(part)&FileAttributes.ReparsePoint)!=0)throw new IOException("Links e pontos de redirecionamento não são aceitos.");
        return full;
    }
    public static FileMessage Read(string path,long offset=0)
    {
        if(offset<0||offset>1000000)throw new IOException("Página inválida.");
        if(string.IsNullOrEmpty(path))return new("listing","",Path:"",Entries:DriveInfo.GetDrives().Where(d=>(d.DriveType is DriveType.Fixed or DriveType.Removable or DriveType.CDRom)&&d.IsReady).Select(d=>new FileEntry("Unidade "+d.Name[..1],d.RootDirectory.FullName,true)).ToArray());
        string directory=Normalize(path);var entries=new List<FileEntry>();long skipped=0;bool more=false;
        foreach(var info in new DirectoryInfo(directory).EnumerateFileSystemInfos())
        {
            try
            {
                if((info.Attributes&FileAttributes.ReparsePoint)!=0)continue;
                if(skipped++<offset)continue;if(entries.Count==100){more=true;break;}
                bool folder=(info.Attributes&FileAttributes.Directory)!=0;
                if(info.FullName.Length<=2048)entries.Add(new(info.Name,info.FullName,folder,folder?0:((FileInfo)info).Length,info.LastWriteTimeUtc));
            }
            catch(UnauthorizedAccessException){}catch(IOException){}
        }
        return new("listing","",Path:directory,Entries:entries.OrderByDescending(e=>e.Directory).ThenBy(e=>e.Name,StringComparer.OrdinalIgnoreCase).ToArray(),Offset:skipped-(more?1:0),More:more);
    }
}
