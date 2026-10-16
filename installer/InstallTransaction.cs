using System.IO;
using System.IO.Compression;

namespace RemoteDesk.Setup;
public static class InstallTransaction
{
    const string MarkerName = ".remotedesk-package";
    const string MarkerValue = "RemoteDesk managed application directory v1";
    public static void Install(Stream payload, string root)
    {
        root = Path.GetFullPath(root);
        CheckParents(root);
        using var archive = new ZipArchive(payload, ZipArchiveMode.Read, leaveOpen: true);
        var entries = PayloadPolicy.Validate(archive);
        Directory.CreateDirectory(root);
        CheckParents(root);
        string app = Path.Combine(root, "app");
        if (Directory.Exists(app))
        {
            CheckTree(app);
            if (!File.Exists(Path.Combine(app, MarkerName)) || File.ReadAllText(Path.Combine(app, MarkerName)) != MarkerValue)
                throw new IOException("O diretório app já existe e não pertence a uma instalação RemoteDesk.");
        }
        else if (File.Exists(app)) throw new IOException("O destino app já é um arquivo.");
        string stage = Path.Combine(root, ".install-" + Guid.NewGuid().ToString("N"));
        string backup = Path.Combine(root, ".previous-" + Guid.NewGuid().ToString("N"));
        bool movedOld = false;
        try
        {
            Directory.CreateDirectory(stage);
            CheckParents(stage);
            foreach (var (entry, relative) in entries)
            {
                string target = Path.Combine(stage, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                CheckParents(Path.GetDirectoryName(target)!);
                if (entry.FullName.EndsWith('/')) { Directory.CreateDirectory(target); continue; }
                using var source = entry.Open();
                using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                source.CopyTo(output);
            }
            File.WriteAllText(Path.Combine(stage, MarkerName), MarkerValue);
            CheckParents(root);
            CheckTree(stage);
            if (Directory.Exists(app)) { CheckTree(app); Directory.Move(app, backup); movedOld = true; }
            try { Directory.Move(stage, app); }
            catch
            {
                if (movedOld && !Directory.Exists(app)) Directory.Move(backup, app);
                throw;
            }
            if (movedOld)
            {
                // Backups contain only installer-managed application binaries; profile is outside app.
                try { DeleteCheckedTree(backup); }
                catch (IOException) { /* A locked backup can remain for manual removal; new install succeeded. */ }
                catch (UnauthorizedAccessException) { }
            }
        }
        finally
        {
            if (Directory.Exists(stage))
            {
                try { DeleteCheckedTree(stage); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }
    static void CheckParents(string path)
    {
        for (string? current = path; current != null; current = Path.GetDirectoryName(current))
            if ((Directory.Exists(current) || File.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Links de diretório não são aceitos na instalação.");
    }
    static void CheckTree(string directory)
    {
        CheckParents(directory);
        foreach (string entry in Directory.EnumerateFileSystemEntries(directory))
        {
            var attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Link encontrado no diretório da aplicação.");
            if ((attributes & FileAttributes.Directory) != 0) CheckTree(entry);
        }
    }
    static void DeleteCheckedTree(string directory)
    {
        CheckTree(directory);
        Directory.Delete(directory, recursive: true);
    }
}
