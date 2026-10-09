using System.CommandLine;
using System.Diagnostics;
using NetISO.Extensions;

var directoryOption = new Option<DirectoryInfo>("--dir")
{
  Description = "The directory to serve, will use current if not set",
  DefaultValueFactory = (_) => new DirectoryInfo(Directory.GetCurrentDirectory())
};

var recurseOption = new Option<bool>("--recurse")
{
  Description = "Serve from subfolders"
};

var serveCommand = new Command("serve", "Start a NetISO server")
{
  Options = { directoryOption, recurseOption }
};
serveCommand.SetAction((parse) => {
  var dir = parse.GetValue(directoryOption) ?? new DirectoryInfo(Directory.GetCurrentDirectory());
  var recurse = parse.GetValue(recurseOption);
  Console.WriteLine($"Starting NetISO server for games under {dir}{(recurse ? " recursively" : "")}");

  var server = new NetISO.Server();
  server.RescanISOs(dir.FullName, recurse);
  server.ServeAsync().Wait();
});


var fileArgument = new Argument<FileInfo>("file")
{
  Description = "The file to read info for"
};

var infoCommand = new Command("info", "Return information about the given game")
{
  Arguments = { fileArgument }
};
infoCommand.SetAction((parse) => {
  var file = parse.GetValue(fileArgument);
  if (file == null)
    throw new ArgumentException("Missing file argument");

  NetISO.IISOSource? iso = null;
  var ext = Path.GetExtension(file.FullName) ?? "";
  switch (ext)
  {
  case ".iso":
    iso = new NetISO.ISOSources.ISOFile(file.FullName);
    break;
  case ".ziso": break;
  case ".zip":
    iso = new NetISO.ISOSources.GODZip(file.FullName);
    break;
  case "":
    iso = new NetISO.ISOSources.GODDir(file.FullName);
    break;
  }

  if (iso == null)
    throw new ArgumentException($"Unknown file type for '{Path.GetFileName(file.FullName)}' ({ext})");

  Console.WriteLine($"{file.FullName}:");

  if (!iso.IsValid)
    throw new ArgumentException("Not a valid Xbox 360 game");

  Console.WriteLine($"  {iso.DisplayName}");
  Console.WriteLine($"  {iso.SectorCount} sectors");
  Console.WriteLine($"  {iso.SectorSize} byte sector size");
  Console.WriteLine($"  {iso.GetByteSize() / 1024 / 1024} MiB disk");
});

var destinationArgument = new Argument<FileInfo>("destination")
{
  Description = "The ISO to create"
};
var blockSizeOption = new Option<uint>("--block-size", "--bs")
{
  Description = "Size per block to copy",
};

var convertCommand = new Command("convert", "Convert an arbitrary source into an ISO")
{
  Arguments = { fileArgument, destinationArgument },
  Options = { blockSizeOption }
};
convertCommand.SetAction((parse) => {
  var source = parse.GetValue(fileArgument);
  if (source == null)
    throw new ArgumentException("Missing source file argument");

  NetISO.IISOSource? iso = null;
  var ext = Path.GetExtension(source.FullName) ?? "";
  switch (ext)
  {
  case ".iso":
    iso = new NetISO.ISOSources.ISOFile(source.FullName);
    break;
  case ".ziso":
    iso = new NetISO.ISOSources.ZISOFile(source.FullName);
    break;
  case ".zip":
    iso = new NetISO.ISOSources.GODZip(source.FullName);
    break;
  case "":
    iso = new NetISO.ISOSources.GODDir(source.FullName);
    break;
  }

  if (iso == null)
    throw new ArgumentException($"Unknown file type for '{Path.GetFileName(source.FullName)}' ({ext})");

  var dest = parse.GetValue(destinationArgument);
  if (dest == null)
    throw new ArgumentException("Missing destination file argument");

  ext = Path.GetExtension(dest.FullName) ?? "";
  if (ext != ".iso")
    throw new ArgumentException("Invalid destination file, must be an ISO");

  var blocksize = parse.GetValue(blockSizeOption);
  if (blocksize <= 0)
    blocksize = iso.SectorSize;

  Console.WriteLine($"Converting {iso.DisplayName} into ISO file {dest.FullName} with block size {blocksize}...");
  var buf = new byte[blocksize];
  var remaining = iso.GetByteSize();
  ulong offset = 0;

  iso.MountAsync().Wait();

  using var destStream = dest.Open(FileMode.Create);
  while (remaining > 0)
  {
    var toRead = (uint)Math.Min(remaining, blocksize);

    var task = iso.ReadAsync(buf, offset, toRead);
    task.Wait();

    System.Diagnostics.Debug.WriteLine($"Read {toRead}B, first 32B are:");
    for (int i = 0; i < 32; ++i)
      System.Diagnostics.Debug.Write($"{buf[i]:X2} ");
    System.Diagnostics.Debug.WriteLine("");
    System.Diagnostics.Debug.WriteLine($"{buf.Count((byte)0)} null bytes");

    destStream.Write(buf, 0, (int)toRead);
    destStream.Flush();
    if (toRead < blocksize || remaining == 0)
      break;

    offset += toRead;
    remaining -= toRead;

    Console.ReadLine();
  }

  Console.WriteLine("Finished copying.");
});

var verboseOption = new Option<bool>("--verbose", "-v")
{
  Description = "Show verbose output"
};

var rootCommand = new RootCommand("A .NET NetISO server and toolkit")
{
  Options = { verboseOption },
  Subcommands = { serveCommand, infoCommand, convertCommand }
};

var parsed = rootCommand.Parse(args);
if (parsed.GetValue(verboseOption))
  Trace.Listeners.Add(new ConsoleTraceListener());

parsed.Invoke();
