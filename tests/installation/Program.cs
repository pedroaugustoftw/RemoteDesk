using Microsoft.Win32;
using RemoteDesk.Desktop;
string fixture=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"artifacts",Guid.NewGuid().ToString("N")));
Directory.CreateDirectory(fixture);
string app=Path.Combine(fixture,"Installed App");Directory.CreateDirectory(app);
File.WriteAllText(Path.Combine(app,InstalledApplication.ClientMarker),"RemoteDesk managed application directory v1");
File.WriteAllText(Path.Combine(app,"RemoteDesk.exe"),"test-owned-binary");File.WriteAllText(Path.Combine(app,"RemoteDesk.Uninstall.exe"),"test-owned-helper");
string data=Path.Combine(fixture,"identity.dpapi");File.WriteAllText(data,"test identity outside application directory");
string testKey=@"Software\RemoteDesk\InstallationTests\"+Guid.NewGuid().ToString("N");
try
{
    using var parent=Registry.CurrentUser.CreateSubKey(testKey);
    string helper=Path.Combine(app,"RemoteDesk.Uninstall.exe"),icon=Path.Combine(app,"RemoteDesk.exe");
    InstalledApplication.Write(parent,"Client","RemoteDesk","1.0.0",app,helper,icon,false,12345);
    InstalledApplication.Write(parent,"Server","RemoteDesk Server","1.0.0",app,helper,icon,true,12345);
    using(var client=parent.OpenSubKey("Client"))
    {
        if(client?.GetValue("DisplayName") as string!="RemoteDesk"||client.GetValue("InstallLocation") as string!=app||client.GetValue("UninstallString") as string!="\""+helper+"\"")throw new Exception("Invalid client installed-app metadata");
        if(client.GetValueKind("EstimatedSize")!=RegistryValueKind.DWord||(int)client.GetValue("NoRepair")! !=1)throw new Exception("Invalid registry value types");
    }
    using(var server=parent.OpenSubKey("Server"))if(server?.GetValue("UninstallString") as string!="\""+helper+"\" --server")throw new Exception("Server uninstall command lost elevation mode");
    InstalledApplication.Write(parent,"Client","RemoteDesk","1.0.1",app,helper,icon,false,12345);
    if(parent.SubKeyCount!=2)throw new Exception("Update duplicated installed-app records");
    Console.WriteLine("PASS real isolated registry: client/server metadata, quoting, version update without duplicate records");
    InstalledApplication.DeleteOwned(app,false);
    if(Directory.Exists(app)||!File.Exists(data))throw new Exception("Uninstall removed identity or left application directory");
    Console.WriteLine("PASS managed application removed while identity outside app directory survives");
    string foreign=Path.Combine(fixture,"foreign");Directory.CreateDirectory(foreign);File.WriteAllText(Path.Combine(foreign,"keep.txt"),"keep");
    try{InstalledApplication.DeleteOwned(foreign,false);throw new Exception("Unmanaged directory removed");}catch(IOException){}
    if(!File.Exists(Path.Combine(foreign,"keep.txt")))throw new Exception("Unmanaged file was deleted");
    try{InstalledApplication.DeleteOwned(Path.GetPathRoot(fixture)!,false);throw new Exception("Drive root allowed");}catch(IOException){}
    Console.WriteLine("PASS unmanaged directory and drive root deletion rejected");
    parent.DeleteSubKeyTree("Client");parent.DeleteSubKeyTree("Server");if(parent.SubKeyCount!=0)throw new Exception("Registry cleanup failed");
    Console.WriteLine("PASS isolated uninstall registration removed; production entries unchanged");
}
finally{Registry.CurrentUser.DeleteSubKeyTree(testKey,false);}
