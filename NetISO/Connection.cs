using System.Buffers.Binary;
using System.Diagnostics;
using System.Net.Sockets;
using NetISO.Extensions;

namespace NetISO;

public class Connection : IDisposable
{
  public Server Server { get; init; }
  public TcpClient Socket { get; init; }

  Stream? stream;
  public Stream Stream => stream ??= Socket.GetStream();

  public event EventHandler? OnDisconnect;

  public IISOSource? ActiveISO { get; set; }

  public System.Net.EndPoint? Endpoint => Socket.Client.RemoteEndPoint;
  
  public Connection(Server Server, TcpClient Socket)
  {
    this.Server = Server;
    this.Socket = Socket;

    Socket.NoDelay = true;
  }

  public bool IsValid => Socket.Connected;

  public async Task PumpConnection()
  {
    using var reader = new Protocol.MessageReader(Stream);
    try
    {
      while (Socket.Connected)
      {
        var msg = await reader.ReadMessageAsync().TimeoutAfter(TimeSpan.FromSeconds(10));

        await HandleMessageAsync(msg);
      }
    }
    catch (TimeoutException)
    {
      Console.WriteLine($"[{Endpoint}] Connection timeout");
      Socket.Close();
    }
    catch (Exception ex)
    {
      Console.WriteLine($"[{Endpoint}] Error in socket:");
      Console.WriteLine(ex);
      Socket.Close();
    }
    finally
    {
      Console.WriteLine($"[{Endpoint}] Disconnected");
      OnDisconnect?.Invoke(this, EventArgs.Empty);
    }
  }

  public async Task HandleMessageAsync(NetISO.Protocol.Message Message)
  {
    Debug.WriteLine($"[{Endpoint}] Received command {Message.Command} (ISO {Message.ISOIndex}, Offset {Message.Offset}, Length {Message.Length})");
    switch (Message.Command)
    {
    case Protocol.Command.Ping: await HandlePingAsync(); return;
    case Protocol.Command.GetIsoSize: await HandleGetIsoSizeAsync(); return;
    case Protocol.Command.HasType1File: await HandleHasType1FileAsync(Message); return;
    case Protocol.Command.ReadData: await HandleReadDataAsync(Message); return;
    case Protocol.Command.GetIsoName: await HandleGetIsoNameAsync(Message); return;
    case Protocol.Command.MountIso: await HandleMountIsoAsync(Message); return;
    }

    Console.WriteLine($"[{Endpoint}] Received unknown command {Message.Command:X}");
    Socket.Client.Close();
  }

  async Task HandlePingAsync()
  {
    var bytes = System.Text.Encoding.ASCII.GetBytes("ISVRokOK");
    await Stream.WriteAsync(bytes);
  }

  async Task HandleGetIsoSizeAsync()
  {
    uint sectors = 0;
    uint sectorSize = 2048;
    if (ActiveISO != null)
    {
      sectors = ActiveISO.SectorCount;
      sectorSize = ActiveISO.SectorSize;
    }

    Debug.WriteLine($"[{Endpoint}] < ISO is {sectors} sectors @ {sectorSize}B");

    var buf = new byte[8];
    BinaryPrimitives.WriteUInt32BigEndian(new Span<byte>(buf, 0, 4), sectors);
    BinaryPrimitives.WriteUInt32BigEndian(new Span<byte>(buf, 4, 4), sectorSize);
    await Stream.WriteAsync(buf);
  }

  async Task HandleHasType1FileAsync(NetISO.Protocol.Message Message)
  {
    var iso = Server.ISOs.ElementAtOrDefault(Message.ISOIndex);

    var buf = new byte[4];
    // buf[0] = (byte)((iso?.HasType1File ?? false) ? 1 : 0);
    await Stream.WriteAsync(buf);
  }

  byte[] readBuffer = new byte[0];
  async Task HandleReadDataAsync(NetISO.Protocol.Message Message)
  {
    if (ActiveISO == null)
      throw new ArgumentNullException();

    if (readBuffer.Length != Message.Length)
      readBuffer = new byte[Message.Length];

    await ActiveISO.ReadAsync(readBuffer, Message.Offset, Message.Length);
    await Stream.WriteAsync(readBuffer);
  }

  async Task HandleGetIsoNameAsync(NetISO.Protocol.Message Message)
  {
    var iso = Server.ISOs.ElementAtOrDefault(Message.ISOIndex);
    Debug.WriteLine($"[{Endpoint}] Retrieving name for ISO {iso} ({iso?.DisplayName})");
    var name = iso?.DisplayName ?? "";

    var buf = new byte[Message.Length];
    if (!string.IsNullOrWhiteSpace(name))
    {
      var filenameBytes = System.Text.Encoding.UTF8.GetBytes(name);
      new ReadOnlySpan<byte>(filenameBytes).CopyTo(new Span<byte>(buf, 0, (int)Message.Length));
    }

    await Stream.WriteAsync(buf);
  }

  async Task HandleMountIsoAsync(NetISO.Protocol.Message Message)
  {
    string isoName;
    using (var reader = new NetISO.Protocol.MessageReader(Stream))
      isoName = await reader.ReadUTF8Async((int)Message.Length);

    string normalized = isoName.Replace(@"\Mount", "").Replace(@"\", "").Replace("\0", "");
    Debug.WriteLine($"[{Endpoint}] Received mount request for '{isoName}', normalized to '{normalized}'");

    uint code = 0;
    if (normalized == "[Disable Current ISO]")
    {
      await ActivateISOAsync(null);
    }
    else
    {
      var iso = Server.ISOs.FirstOrDefault(isoSource => isoSource.DisplayName.EndsWith(normalized, StringComparison.CurrentCultureIgnoreCase));
      if (iso != null)
      {
        try
        {
          await ActivateISOAsync((IISOSource)iso.Clone());
          if (ActiveISO == null)
            throw new ApplicationException("Failed to activate ISO");

          Debug.WriteLine($"[{Endpoint}] Mounting {iso}...");
          await ActiveISO.MountAsync();

          Trace.WriteLine($"[{Endpoint}] Mounted {iso}");

          code = 1;
        }
        catch (Exception ex)
        {
          Trace.WriteLine($"[{Endpoint}] Failed to mount {iso}, {ex}");
        }
      }
      else
        Trace.WriteLine($"[{Endpoint}] Failed to find ISO for '{normalized}'");
    }

    var buf = new byte[4];
    BinaryPrimitives.WriteUInt32BigEndian(new Span<byte>(buf), code);
    await Stream.WriteAsync(buf);
  }

  async Task ActivateISOAsync(IISOSource? ISO)
  {
    if (ISO == null)
      Debug.WriteLine($"[{Endpoint}] Deactivating ISO");
    else
      Debug.WriteLine($"[{Endpoint}] Activating {ISO} ({ISO.DisplayName})");
    await (ActiveISO?.UnmountAsync() ?? Task.CompletedTask);
    if (ActiveISO is IAsyncDisposable disposableAsync)
      await disposableAsync.DisposeAsync();
    if (ActiveISO is IDisposable disposable)
      disposable.Dispose();

    ActiveISO = ISO;
  }
  
  public void Dispose()
  {
    var unmount = ActiveISO?.UnmountAsync() ?? Task.CompletedTask;
    unmount.Wait();
    if (ActiveISO is IDisposable disposable)
      disposable.Dispose();
    ActiveISO = null;

    Socket.Dispose();
  }
}
