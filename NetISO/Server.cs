using System.Net;
using System.Net.Sockets;

namespace NetISO;

public class Server
{
  public IList<IISOSource> ISOs { get; private set; } = new IISOSource[0];

  public IPAddress Listen { get; set; } = IPAddress.Any;
  public ushort Port { get; set; } = 4323;

  Dictionary<Connection, Task> activeConnections = new Dictionary<Connection, Task>();

  public Server()
  {

  }

  public void RescanISOs(string Path, bool Recursive)
  {
    IEnumerable<IISOSource> scanned = new IISOSource[0];

    scanned = scanned.Concat(ISOSources.GODDir.DiscoverFiles(Path, Recursive));
    scanned = scanned.Concat(ISOSources.GODZip.DiscoverFiles(Path, Recursive));
    scanned = scanned.Concat(ISOSources.ISOFile.DiscoverFiles(Path, Recursive));
    scanned = scanned.Concat(ISOSources.ZISOFile.DiscoverFiles(Path, Recursive));

    ISOs = scanned.OrderBy(iso => iso.DisplayName).AsParallel().ToList();

    System.Diagnostics.Trace.WriteLine($"Discovered {ISOs} games");
  }

  public async Task ServeAsync()
  {
    var listener = new TcpListener(Listen, Port);
    listener.Start();

    Console.WriteLine($"Listening on {listener.LocalEndpoint}");

    while (true)
    {
      var client = await listener.AcceptTcpClientAsync();
      var connection = new Connection(this, client);
      connection.OnDisconnect += (_, _) => {
        Console.WriteLine($"Closing connection from {client.Client.RemoteEndPoint}");
        connection.Dispose();
        activeConnections.Remove(connection);
      };

      Console.WriteLine($"Accepted connection from {client.Client.RemoteEndPoint}");
      activeConnections.Add(connection, connection.PumpConnection());
    }
  }
}
