using System.Diagnostics;
using NetISO.Extensions;

namespace NetISO.Definitions;

public class GDFX : IDisposable, ICloneable
{
  public const int SectorSize = 0x800; // 2048 bytes
                                        //
  public string Title { get; private init; }
  public long Size { get; private init; }

  public bool IsValid { get; private set; } = false;

  public Stream ISOStream { get; private set; }

  public GDFX(Stream ISOStream, string Title)
  {
    this.ISOStream = ISOStream;
    this.Title = Title;
    this.Size = ISOStream.Length;
  }

  public GDFX(Stream ISOStream, GDFX CopyOf)
  {
    this.ISOStream = ISOStream;
    this.Title = CopyOf.Title;
    this.Size = CopyOf.Size;
    this.IsValid = CopyOf.IsValid;
  }

  public object Clone()
  {
    return new GDFX(Stream.Null, this);
  }

  public static GDFX Invalid => new GDFX(Stream.Null, "<Invalid>");

  public async Task ValidateAsync(CancellationToken ct = default)
  {
    if (IsValid)
      return;

    if (!ISOStream.CanSeek)
      return;

    if (!await VerifyFileAsync())
      return;

    IsValid = true;
  }

  public void Dispose()
  {
    ISOStream.Dispose();
    ISOStream = Stream.Null;
  }

  async Task<bool> VerifyFileAsync()
  {
    using var reader = new BigEndianBinaryReader(ISOStream);
    
    var offsets = new[] {
      0x00000000, 0x0000FB20, 0x00020600, 0x02080000, 0x0FD90000
    };
    var buf = new byte[20];
    var dataOffset = 0;
    foreach (var offset in offsets)
    {
      if (offset > ISOStream.Length)
        return false;

      dataOffset = offset;
      ISOStream.Seek(offset + 32 * SectorSize, SeekOrigin.Begin);

      buf = reader.ReadBytes(20);
      if (System.Text.Encoding.ASCII.GetString(buf, 0, 20) == Definitions.XGD.Magic)
      {
        Debug.WriteLine($"Found XGD magic at offset {dataOffset:X}");
        break;
      }
    }

    var rootSector = reader.ReadUInt32();
    var rootSize = reader.ReadUInt32();
    Debug.WriteLine($"ISO root sector {rootSector}, size {rootSize}");

    if (rootSize < 13 || rootSize > 32 * 1024 * 1024)
    {
      Debug.WriteLine($"Invalid root size {rootSize}");
      return false;
    }

    return true;
  }
}
