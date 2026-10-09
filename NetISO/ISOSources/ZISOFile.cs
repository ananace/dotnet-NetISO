using ZArchiveSharp;

namespace NetISO.ISOSources;

public class ZISOFile : IISOSource
{
  public static IEnumerable<ZISOFile> DiscoverFiles(string Path, bool Recurse)
  {
    var dir = new DirectoryInfo(Path);
    if (!dir.Exists)
      yield break;

    var opts = new EnumerationOptions();
    opts.MatchCasing = MatchCasing.CaseInsensitive;
    opts.MatchType = MatchType.Simple;
    opts.RecurseSubdirectories = Recurse;

    foreach (var file in dir.EnumerateFiles("*.ziso", opts))
    {
      var source = new ZISOFile(file.FullName);
      if (!source.IsValid)
        continue;

      yield return source;
    }
  }

  public string DisplayName => $"{GDFX.Title} (ISO)";
  public uint SectorSize { get; set; } = (uint)Definitions.GDFX.SectorSize;
  public uint SectorCount => (uint)Math.Ceiling(GDFX.Size / (double)SectorSize);

  public bool IsValid  => GDFX.IsValid;

  string ZISOPath;
  ZArchiveReader Archive;
  Definitions.GDFX GDFX;
  uint ISOEntry;

  public ZISOFile(string Path)
  {
    ZISOPath = Path;
    var archive = ZArchiveReader.TryOpen(ZISOPath);
    if (archive == null)
      throw new ArgumentException("Unable to open ziso", nameof(Path));

    Archive = archive;
    var isoEntry = FindISO();
    if (!isoEntry.HasValue)
    {
      GDFX = Definitions.GDFX.Invalid;
      return;
    }

    ISOEntry = isoEntry.Value;
    GDFX = new Definitions.GDFX(archive.OpenRead(ISOEntry), System.IO.Path.GetFileNameWithoutExtension(ZISOPath));
    GDFX.ValidateAsync().Wait();
  }

  ZISOFile(string Path, Definitions.GDFX GDFX)
  {
    this.ZISOPath = Path;

    var archive = ZArchiveReader.TryOpen(ZISOPath);
    if (archive == null)
      throw new ArgumentException("Unable to open ziso", nameof(Path));
    Archive = archive;
    var isoEntry = FindISO();
    if (!isoEntry.HasValue)
    {
      this.GDFX = Definitions.GDFX.Invalid;
      return;
    }

    ISOEntry = isoEntry.Value;
    this.GDFX = GDFX;
  }

  uint? FindISO()
  {
    uint? isoEntry = null;
    for (uint i = 0; i < Archive.EntryCount; ++i)
    {
      if (!Archive.IsFile(i))
        continue;

      if (!Archive.TryGetNodeName(i, out string name))
        continue;

      if (name.EndsWith(".iso", StringComparison.CurrentCultureIgnoreCase))
      {
        isoEntry = i;
        break;
      }
    }
    return isoEntry;
  }

  public object Clone()
  {
    return new ZISOFile(ZISOPath, (Definitions.GDFX)GDFX.Clone());
  }

  Stream? Stream;
  public async Task MountAsync()
  {
    if (Stream != null)
      await Stream.DisposeAsync();
    Stream = Archive.OpenRead(ISOEntry);
  }
  public async Task UnmountAsync()
  {
    if (Stream != null)
      await Stream.DisposeAsync();
    Stream = null;
  }

  public async Task ReadAsync(byte[] buf, ulong offset, uint length, CancellationToken ct)
  {
    if (Stream == null)
      throw new ArgumentNullException();

    Stream.Seek((long)offset, SeekOrigin.Begin);
    await Stream.ReadAtLeastAsync(buf, (int)length, false, ct);
  }
}
