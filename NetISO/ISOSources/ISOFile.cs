namespace NetISO.ISOSources;

public class ISOFile : IISOSource
{
  public static IEnumerable<ISOFile> DiscoverFiles(string Path, bool Recurse)
  {
    var dir = new DirectoryInfo(Path);
    if (!dir.Exists)
      yield break;

    var opts = new EnumerationOptions();
    opts.MatchCasing = MatchCasing.CaseInsensitive;
    opts.MatchType = MatchType.Simple;
    opts.RecurseSubdirectories = Recurse;

    foreach (var file in dir.EnumerateFiles("*.iso", opts))
    {
      var source = new ISOFile(file.FullName);
      if (!source.IsValid)
        continue;

      yield return source;
    }
  }

  public string DisplayName => $"{GDFX.Title} (ISO)";
  public uint SectorSize { get; set; } = (uint)Definitions.GDFX.SectorSize;
  public uint SectorCount => (uint)Math.Ceiling(GDFX.Size / (double)SectorSize);

  public bool IsValid  => GDFX.IsValid;

  string ISOPath;
  Definitions.GDFX GDFX;

  // int? DataOffset = null;
  public ISOFile(string Path)
  {
    ISOPath = Path;

    var f = new FileInfo(ISOPath);
    System.Diagnostics.Debug.WriteLine($"For {Path}");
    GDFX = new Definitions.GDFX(f.OpenRead(), System.IO.Path.GetFileNameWithoutExtension(ISOPath));
    GDFX.ValidateAsync().Wait();
  }

  ISOFile(string Path, Definitions.GDFX GDFX)
  {
    this.ISOPath = Path;
    this.GDFX = GDFX;
  }

  public object Clone()
  {
    return new ISOFile(ISOPath, (Definitions.GDFX)GDFX.Clone());
  }

  FileStream? Stream;
  public async Task MountAsync()
  {
    if (Stream != null)
      await Stream.DisposeAsync();
    Stream = File.OpenRead(ISOPath);
    System.Diagnostics.Debug.WriteLine($"Opened {ISOPath} for read, stream is {Stream.Length}B long");
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
    await Stream.ReadExactlyAsync(buf, 0, (int)length, ct);
  }
}
