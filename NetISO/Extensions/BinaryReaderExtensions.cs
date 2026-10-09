namespace NetISO.Extensions;

public static class BinaryReaderExtensions
{
  public static int ReadInt24(this BinaryReader Reader)
  {
    var buf = Reader.ReadBytes(3);
    return buf[0] | buf[1] << 8 | buf[2] << 16;
  }
  public static uint ReadUInt24(this BinaryReader Reader)
  {
    var buf = Reader.ReadBytes(3);
    return (uint)(buf[0] | buf[1] << 8 | buf[2] << 16);
  }
}
