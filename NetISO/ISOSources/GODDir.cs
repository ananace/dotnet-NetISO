using System.Diagnostics;

namespace NetISO.ISOSources;

public class GODDir : IISOSource
{
  public static IEnumerable<GODDir> DiscoverFiles(string Path, bool Recurse)
  {
    var dir = new DirectoryInfo(Path);
    if (!dir.Exists)
      yield break;

    var opts = new EnumerationOptions();
    opts.MatchCasing = MatchCasing.CaseInsensitive;
    opts.MatchType = MatchType.Simple;
    opts.RecurseSubdirectories = true; // Recurse;

    Debug.WriteLine("Searching for GOD directories");
    foreach (var datadir in dir.EnumerateDirectories("*.data", opts))
    {
      var source = new GODDir(datadir.FullName.Replace(".data", ""));
      if (!source.IsValid)
        continue;

      yield return source;
    }
  }

  public string DisplayName => $"{STFS.Title} (GOD)";
  public uint SectorSize => (uint)Definitions.STFS.BLOCK_SIZE;
  public uint SectorCount => (uint)(STFS.ByteSize / SectorSize);

  public bool IsValid => STFS.IsValid;
  public string BasePath { get; private init; }

  Definitions.STFS STFS;

  public GODDir(string GODDir)
  {
    Debug.WriteLine($"Looking for GOD under {GODDir}");
    var attr = File.GetAttributes(GODDir);
    if (attr.HasFlag(FileAttributes.Directory))
    {
      if (GODDir.EndsWith(".data"))
        GODDir = Path.GetDirectoryName(GODDir) ?? GODDir;
      else
      {
        var entries = Directory.GetFileSystemEntries(GODDir);
        while (entries.Length == 1)
        {
          if (!Directory.Exists(entries[0]))
            break;

          GODDir = entries[0];
          Debug.WriteLine($"  Checking {GODDir}");
          entries = Directory.GetFileSystemEntries(GODDir);
        }

        GODDir = Directory.GetFiles(GODDir)[0];
      }
    }

    BasePath = GODDir;
    Debug.WriteLine($"Assuming base path as {BasePath}");

    STFS = Definitions.STFS.LoadFromDir(BasePath);
    STFS.ValidateAsync().Wait();
  }

  GODDir(string GODDir, Definitions.STFS STFS)
  {
    this.BasePath = GODDir;
    this.STFS = STFS;
  }

  public object Clone()
  {
    return new GODDir(BasePath, new Definitions.STFS((path) => File.OpenRead(path), (path) => new FileInfo(path).Length, STFS));
  }

  public Task MountAsync() => Task.CompletedTask;
  public Task UnmountAsync() => Task.CompletedTask;

  public async Task ReadAsync(byte[] buf, ulong offset, uint length, CancellationToken ct)
    => await STFS.ReadBytesAsync(buf, (long)offset, length, ct);
}
