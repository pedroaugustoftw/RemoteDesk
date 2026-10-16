using System.IO;
using System.IO.Compression;

namespace RemoteDesk.Setup;
public static class PayloadPolicy
{
    public const long MaximumExpandedBytes = 1_500_000_000;
    public const int MaximumEntries = 20000;
    public static IReadOnlyList<(ZipArchiveEntry Entry, string RelativePath)> Validate(ZipArchive archive, string requiredExecutable = "RemoteDesk.exe")
    {
        if (archive.Entries.Count > MaximumEntries) throw new InvalidDataException("Pacote com arquivos demais.");
        var result = new List<(ZipArchiveEntry, string)>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (var entry in archive.Entries)
        {
            string path = entry.FullName.Replace('\\', '/');
            if (path.StartsWith('/') || path.Contains(':') || path.Contains('\0')) throw new InvalidDataException("Caminho inválido no pacote.");
            string[] segments = path.TrimEnd('/').Split('/');
            if (segments.Length == 0 || segments.Any(UnsafeSegment)) throw new InvalidDataException("Caminho inválido no pacote.");
            string normalized = string.Join(Path.DirectorySeparatorChar, segments);
            if (!names.Add(normalized)) throw new InvalidDataException("Caminho duplicado no pacote.");
            string last = segments[^1];
            if (last.Equals("settings.json", StringComparison.OrdinalIgnoreCase) || last.Equals("identity.dpapi", StringComparison.OrdinalIgnoreCase) || last.Equals("events.log", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Pacote não pode incluir dados pessoais do cliente.");
            int unixType = (entry.ExternalAttributes >> 16) & 0xF000;
            if (unixType == 0xA000 || (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Links não são aceitos no pacote.");
            total = checked(total + entry.Length);
            if (total > MaximumExpandedBytes) throw new InvalidDataException("Pacote excede o tamanho permitido.");
            result.Add((entry, normalized));
        }
        // Detect file/directory aliases before extraction, regardless of archive ordering.
        var files = result.Where(x => !x.Item1.FullName.EndsWith('/')).Select(x => x.Item2).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var (_, relative) in result)
            for (string? parent = Path.GetDirectoryName(relative); !string.IsNullOrEmpty(parent); parent = Path.GetDirectoryName(parent))
                if (files.Contains(parent)) throw new InvalidDataException("Conflito entre arquivo e diretório.");
        if (!result.Any(x => x.Item2.Equals(requiredExecutable.Replace('/', Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase) && x.Item1.Length > 0))
            throw new InvalidDataException("Executável ausente do pacote.");
        return result;
    }
    private static bool UnsafeSegment(string segment)
    {
        if (segment.Length == 0 || segment is "." or ".." || segment.EndsWith('.') || segment.EndsWith(' ') || segment.Any(c => c < 32 || "<>\"|?*".Contains(c))) return true;
        string stem = segment.Split('.')[0];
        return stem.Equals("CON", StringComparison.OrdinalIgnoreCase) || stem.Equals("PRN", StringComparison.OrdinalIgnoreCase) || stem.Equals("AUX", StringComparison.OrdinalIgnoreCase) || stem.Equals("NUL", StringComparison.OrdinalIgnoreCase)
            || (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase) || stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) && stem[3] is >= '1' and <= '9');
    }
}
