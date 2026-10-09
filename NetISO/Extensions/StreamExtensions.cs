using System.Text;

namespace NetISO.Extensions;

public static class StreamExtensions
{
  public static async Task<string> ReadWStringAsync(this Stream Stream, int len)
  {
    var ret = new StringBuilder();
    var buf = new byte[2];

    if (len == 0)
      return ret.ToString();
    else
    {
      var enc = new UnicodeEncoding(true, false);
      int read = 0;
      while (true)
      {
        var bytes = await Stream.ReadAsync(buf, 0, 2);
        if (bytes < 2)
          break;
        var dec = enc.GetString(buf, 0, 2);
        if (dec == "\0")
          break;

        ret.Append(dec);
        if (len > 0 && ++read >= len)
          break;
      }
    }

    return ret.ToString();
  }

  public static async Task<long> SeekForwardsAsync(this Stream Stream, long Offset, CancellationToken cts = default)
  {
    if (Offset == 0)
      return 0 ;
    else if (Offset < 0)
      throw new ArgumentException("Unable to seek by negative offset", nameof(Offset));
    else if (Stream.CanSeek)
    {
      var before = Stream.Position;
      Stream.Seek(Offset, SeekOrigin.Current);
      var after = Stream.Position;
      return after - before;
    }

    var totalRead = 0;
    var read = 0;
    var buf = new byte[1024];
    var origOffset = Offset;
    while (Offset > 1024)
    {
      read = await Stream.ReadAsync(buf, 0, 1024, cts);
      totalRead += read;
      if (read == 0)
        return totalRead;

      Offset -= read;
    }

    while (Offset > 0)
    {
      read = await Stream.ReadAsync(buf, 0, (int)Offset, cts);
      totalRead += read;
      if (read == 0)
        return totalRead;

      Offset -= read;
    }

    return totalRead;
  }
}
