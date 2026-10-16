using System.Windows;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
namespace RemoteDesk.Setup;
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        if (e.Args.Contains("--verify-payload"))
        {
            // Read-only packaging check: never extracts, installs or creates shortcuts.
            try
            {
                using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("RemoteDesk.Payload.zip") ?? throw new InvalidOperationException();
                using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
                _ = PayloadPolicy.Validate(archive);
                if(!archive.Entries.Any(entry=>entry.FullName.Equals("RemoteDesk.Uninstall.exe",StringComparison.OrdinalIgnoreCase)&&entry.Length>0))throw new InvalidOperationException("Desinstalador ausente.");
                int expectedIndex = Array.IndexOf(e.Args, "--expected-client-sha256");
                string? expectedHash = expectedIndex >= 0 ? e.Args[expectedIndex + 1] : null;
                foreach (var entry in archive.Entries)
                {
                    using var content = entry.Open();
                    string hash = Convert.ToHexString(SHA256.HashData(content));
                    if (entry.FullName.Equals("RemoteDesk.exe", StringComparison.OrdinalIgnoreCase) && expectedHash != null && !hash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException();
                }
                Shutdown(0);
            }
            catch { Shutdown(1); }
            return;
        }
        base.OnStartup(e);
    }
}
