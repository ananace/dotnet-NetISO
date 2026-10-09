using System.Diagnostics;
using System.IO.Compression;

namespace NetISO.ISOSources;

public class GODZip : IISOSource
{
  public static IEnumerable<GODZip> DiscoverFiles(string Path, bool Recurse)
  {
    var dir = new DirectoryInfo(Path);
    if (!dir.Exists)
      yield break;

    var opts = new EnumerationOptions();
    opts.MatchCasing = MatchCasing.CaseInsensitive;
    opts.MatchType = MatchType.Simple;
    opts.RecurseSubdirectories = Recurse;

    Debug.WriteLine("Searching for GOD zip files");
    foreach (var zip in dir.EnumerateFiles("*.zip", opts))
    {
      var source = new GODZip(zip.FullName);
      if (!source.IsValid)
        continue;

      yield return source;
    }
  }

  public string DisplayName => $"{STFS.Title} (GOD+ZIP)";
  public uint SectorSize => (uint)Definitions.STFS.BLOCK_SIZE;
  public uint SectorCount => (uint)(STFS.ByteSize / SectorSize);

  public bool IsValid => STFS.IsValid;

  string archivePath;
  string? basePath = null;
  Definitions.STFS STFS;

  public GODZip(string GODZip)
  {
    archivePath = GODZip;
    basePath = "";

    try
    {
      Debug.WriteLine($"Searching {archivePath} for GOD");
      using var archive = ZipFile.OpenRead(archivePath);

      foreach (var entry in archive.Entries)
      {
        if (entry.Length == 0)
          continue;
        if (entry.FullName.Contains(".data"))
          continue;

        Debug.WriteLine($"  Assuming {entry.FullName}");
        basePath = entry.FullName;
        break;
      }

      if (basePath == null)
      {
        Debug.WriteLine("Failed to find GOD in archive");
        STFS = Definitions.STFS.Invalid;
        return;
      }

      Debug.WriteLine($"Assuming main GOD container at {basePath}");
      STFS = Definitions.STFS.LoadFromArchive(archivePath, basePath);
      STFS.ValidateAsync().Wait();
    }
    catch (Exception ex)
    {
      Console.WriteLine($"Failed to load GOD from {archivePath};\n{ex}");
      STFS = Definitions.STFS.Invalid;
    }
  }

  GODZip(string GODZip, string? BasePath, Definitions.STFS STFS)
  {
    this.archivePath = GODZip;
    this.basePath = BasePath;
    this.STFS = STFS;
  }

  public object Clone()
  {
    var arch = ZipFile.OpenRead(archivePath);
    return new GODZip(archivePath, basePath,
      new Definitions.STFS(
        (path) => { var entry = arch.GetEntry(path); return entry?.Open(); },
        (path) => { var entry = arch.GetEntry(path); return entry?.Length; },
        STFS,
        arch
      )
    );
  }

  public Task MountAsync() => Task.CompletedTask;
  public Task UnmountAsync() => Task.CompletedTask;
  public async Task ReadAsync(byte[] buf, ulong offset, uint length, CancellationToken ct)
    => await STFS.ReadBytesAsync(buf, (long)offset, length, ct);
}
