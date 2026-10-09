using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;

namespace NetISO.Protocol;

/// Reads BigEndian NetISO Messages
public class MessageReader : IDisposable
{
  public Stream BaseStream { get; private init; }
  public bool KeepOpen { get; set; } = true;

  byte[]? readBuf;

  public MessageReader(Stream Stream)
  {
    BaseStream = Stream;
  }

  public async Task<Message> ReadMessageAsync(CancellationToken cancellationToken = default)
  {
    var magic = await ReadASCIIAsync(4);
    if (magic != "ISVR")
      throw new InvalidDataException($"Received invalid magic '{magic}'");

    var command = (Command)(await ReadUInt16Async());
    var iso = await ReadUInt16Async();
    var offset = await ReadUInt64Async();
    var length = await ReadUInt32Async();

    if (command == Command.Ping || command == Command.ReadData || command == Command.GetIsoSize)
      iso = 0;

    return new Message {
      Command = command,
      ISOIndex = iso,
      Offset = offset,
      Length = length
    };
  }

  public Message ReadMessage()
  {
    var task = ReadMessageAsync();
    task.Wait();
    return task.Result;
  }

  public async Task<bool> ReadBoolAsync()
  {
    await FillBufferAsync(1);
    return readBuf[0] != 0;
  }

  public async Task<byte> ReadByteAsync()
  {
    await FillBufferAsync(1);
    return readBuf[0];
  }

  public async Task<ushort> ReadUInt16Async()
  {
    await FillBufferAsync(2);
    return BinaryPrimitives.ReadUInt16BigEndian(new ReadOnlySpan<byte>(readBuf, 0, 2));
  }

  public async Task<uint> ReadUInt32Async()
  {
    await FillBufferAsync(4);
    return BinaryPrimitives.ReadUInt32BigEndian(new ReadOnlySpan<byte>(readBuf, 0, 4));
  }

  public async Task<ulong> ReadUInt64Async()
  {
    await FillBufferAsync(8);
    return BinaryPrimitives.ReadUInt64BigEndian(new ReadOnlySpan<byte>(readBuf, 0, 8));
  }

  public async Task<string> ReadASCIIAsync(int bytes)
  {
    await FillBufferAsync(bytes);
    return System.Text.Encoding.ASCII.GetString(readBuf, 0, bytes);
  }

  public async Task<string> ReadUTF8Async(int bytes)
  {
    await FillBufferAsync(bytes);
    return System.Text.Encoding.UTF8.GetString(readBuf, 0, bytes);
  }

  // Read bytes using a shared 128B buffer for performance
  [MemberNotNull(nameof(readBuf))]
  private async Task<int> ReadBytesAsync(byte[] dest, int index, int count)
  {
    if (readBuf == null)
      readBuf = new byte[128];

    int totalRemaining = count;
    while (totalRemaining > 0)
    {
      int bytesRead = await BaseStream.ReadAsync(readBuf, 0, Math.Min(totalRemaining, 128));

      if (bytesRead == 0)
        return count - totalRemaining;

      readBuf.CopyTo(dest, index);

      totalRemaining -= bytesRead;
      index += bytesRead;
    }

    return count - totalRemaining;
  }
  
  [MemberNotNull(nameof(readBuf))]
  private async Task FillBufferAsync(int count)
  {
    if (readBuf == null)
      readBuf = new byte[128];

    if (count == 1)
    {
      var b = BaseStream.ReadByte();
      if (b == -1)
        throw new EndOfStreamException();
      readBuf[0] = (byte)b;
      return;
    }

    int bytesRead = 0;
    do
    {
      int n = await BaseStream.ReadAsync(readBuf, bytesRead, count - bytesRead);
      if (n == 0)
        throw new EndOfStreamException();
      bytesRead += n;
    } while (bytesRead < count);
  }

  public void Dispose()
  {
    readBuf = null;
    if (!KeepOpen)
    {
      BaseStream.Close();
      BaseStream.Dispose();
    }
  }
}
