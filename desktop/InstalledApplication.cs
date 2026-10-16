using Microsoft.Win32;
using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace RemoteDesk.Desktop;

public static class InstalledApplication
{
    public const string ClientMarker=".remotedesk-package";
    public const string ServerMarker=".remotedesk-server-owned";
    public const string UninstallBranch=@"Software\Microsoft\Windows\CurrentVersion\Uninstall";
    public static string ClientRoot=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"RemoteDesk","app");
    public static string ServerRoot=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"RemoteDesk Server");
    public static string KeyName(bool server)=>server?"RemoteDesk.Server":"RemoteDesk.Client";
    public static string Helper(bool server)=>Path.Combine(server?ServerRoot:ClientRoot,server?"uninstaller":"", "RemoteDesk.Uninstall.exe");
    public static void Register(bool server)
    {
        string root=server?ServerRoot:ClientRoot;
        string icon=server?Helper(true):Path.Combine(root,"RemoteDesk.exe");
        CheckOwned(root,server);
        if(!File.Exists(Helper(server)))throw new IOException("Desinstalador ausente. Atualize usando o pacote completo.");
        var version=FileVersionInfo.GetVersionInfo(Helper(server)).ProductVersion?.Split('+')[0]??"1.0.0";
        long bytes=Directory.EnumerateFiles(root,"*",SearchOption.AllDirectories).Sum(f=>new FileInfo(f).Length);
        using var hive=RegistryKey.OpenBaseKey(server?RegistryHive.LocalMachine:RegistryHive.CurrentUser,RegistryView.Registry64);
        using var branch=hive.CreateSubKey(UninstallBranch);
        Write(branch,KeyName(server),server?"RemoteDesk Server":"RemoteDesk",version,root,Helper(server),icon,server,bytes);
    }
    public static void Write(RegistryKey parent,string name,string displayName,string version,string root,string helper,string icon,bool server,long bytes)
    {
        root=Path.GetFullPath(root);helper=Path.GetFullPath(helper);icon=Path.GetFullPath(icon);
        if(helper.Contains('"')||icon.Contains('"'))throw new ArgumentException("Caminho de instalação inválido.");
        using var key=parent.CreateSubKey(name);
        key.SetValue("DisplayVersion",version);key.SetValue("Publisher","RemoteDesk");
        key.SetValue("InstallLocation",root);key.SetValue("DisplayIcon","\""+icon+"\",0");
        key.SetValue("UninstallString","\""+helper+"\""+(server?" --server":""));
        key.SetValue("InstallDate",DateTime.UtcNow.ToString("yyyyMMdd",CultureInfo.InvariantCulture));
        key.SetValue("EstimatedSize",(int)Math.Clamp((bytes+1023)/1024,1,int.MaxValue),RegistryValueKind.DWord);
        key.SetValue("NoModify",1,RegistryValueKind.DWord);key.SetValue("NoRepair",1,RegistryValueKind.DWord);
        key.SetValue("DisplayName",displayName);
    }
    public static void Unregister(bool server)
    {
        using var hive=RegistryKey.OpenBaseKey(server?RegistryHive.LocalMachine:RegistryHive.CurrentUser,RegistryView.Registry64);
        using var branch=hive.OpenSubKey(UninstallBranch,true);branch?.DeleteSubKeyTree(KeyName(server),false);
    }
    public static void CheckOwned(string root,bool server)
    {
        root=Path.GetFullPath(root);CheckNoLinks(root);
        string marker=Path.Combine(root,server?ServerMarker:ClientMarker);
        string expected=server?"RemoteDesk Server":"RemoteDesk managed application directory v1";
        if(!File.Exists(marker)||File.ReadAllText(marker)!=expected)throw new IOException("Pasta sem identificação de instalação RemoteDesk: "+root);
        CheckTree(root);
    }
    public static void DeleteOwned(string root,bool server)
    {
        root=Path.GetFullPath(root);
        if(Path.GetDirectoryName(root) is null)throw new IOException("Não é permitido remover a raiz de uma unidade.");
        CheckOwned(root,server);
        string marker=Path.Combine(root,server?ServerMarker:ClientMarker);
        foreach(string entry in Directory.EnumerateFileSystemEntries(root))
        {
            if(entry.Equals(marker,StringComparison.OrdinalIgnoreCase))continue;
            if(Directory.Exists(entry))Directory.Delete(entry,true);else File.Delete(entry);
        }
        File.Delete(marker);Directory.Delete(root,false);
    }
    public static void CheckNoLinks(string path)
    {
        for(string? current=Path.GetFullPath(path);current is not null;current=Path.GetDirectoryName(current))
            if((File.Exists(current)||Directory.Exists(current))&&(File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0)throw new IOException("Links não são aceitos no diretório de instalação.");
    }
    static void CheckTree(string root)
    {
        foreach(string item in Directory.EnumerateFileSystemEntries(root))
        {
            var attributes=File.GetAttributes(item);if((attributes&FileAttributes.ReparsePoint)!=0)throw new IOException("Link encontrado na instalação.");
            if((attributes&FileAttributes.Directory)!=0)CheckTree(item);
        }
    }
}
