using System.Buffers.Binary;

namespace NetISO.Extensions;

public class BigEndianBinaryReader : BinaryReader
{
  public BigEndianBinaryReader(Stream Stream) : base(Stream) { }

  public override short ReadInt16()
    => BinaryPrimitives.ReadInt16BigEndian(ReadBytes(2));
  public override ushort ReadUInt16()
    => BinaryPrimitives.ReadUInt16BigEndian(ReadBytes(2));
  public int ReadInt24()
  {
    var buf = ReadBytes(3);
    return buf[0] << 16 | buf[1] << 8 | buf[2];
  }
  public uint ReadUInt24()
  {
    var buf = ReadBytes(3);
    return (uint)(buf[0] << 16 | buf[1] << 8 | buf[2]);
  }
  public override int ReadInt32()
    => BinaryPrimitives.ReadInt32BigEndian(ReadBytes(4));
  public override uint ReadUInt32()
    => BinaryPrimitives.ReadUInt32BigEndian(ReadBytes(4));
  public override long ReadInt64()
    => BinaryPrimitives.ReadInt64BigEndian(ReadBytes(8));
  public override ulong ReadUInt64()
    => BinaryPrimitives.ReadUInt64BigEndian(ReadBytes(8));
}
