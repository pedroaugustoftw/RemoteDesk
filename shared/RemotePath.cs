namespace RemoteDesk.Shared;
// Remote paths must be interpreted using the destination's syntax, not the viewer OS.
public static class RemotePath
{
    public static string Combine(string directory,string name) => directory.StartsWith('/') ? directory.TrimEnd('/')+"/"+name : System.IO.Path.Combine(directory,name);
    public static string Parent(string path)
    {
        if(!path.StartsWith('/'))return System.IO.Directory.GetParent(path)?.FullName??"";
        if(path=="/")return "";
        int slash=path.TrimEnd('/').LastIndexOf('/');return slash<=0?"/":path[..slash];
    }
}
