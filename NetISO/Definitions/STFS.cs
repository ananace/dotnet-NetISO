using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;
using NetISO.Extensions;

namespace NetISO.Definitions;

public class STFS : IDisposable
{
  enum ContentPackageType
  {
    CON = 0x434F4E20,
    LIVE = 0x4C495645,
    PIRS = 0x50495253,
  }

  enum ContentVolumeType
  {
    STFS = 0,
    SVOD = 1,
    FATX = 2
  }

  enum SVODLayout
  {
    Unknown,
    EGDF,
    XSF,
    SingleFile,
  }

  Func<string,Stream?> streamFactory;
  Func<string,long?> sizeFactory;
  IDisposable? disposableExtra = null;
  string basePath;

  ContentVolumeType volumeType;
  SVODLayout svodLayout;
  int contentFileCount;
  int dataBlockCount;
  int startDataBlock;
  long svodBaseOffset;
  string title = "<Invalid>";

  public bool IsValid { get; private set; } = false;

  public string Title => title;
  public ulong ByteSize;
  public const long BLOCK_SIZE = 0x800;

  public STFS(Func<string,Stream?> StreamFactory, Func<string,long?> SizeFactory, string BasePath, IDisposable? DisposableExtra = null)
  {
    streamFactory = StreamFactory;
    sizeFactory = SizeFactory;
    basePath = BasePath;
    disposableExtra = DisposableExtra;
  }

  public STFS(Func<string,Stream?> StreamFactory, Func<string,long?> SizeFactory, STFS CopyOf, IDisposable? DisposableExtra = null)
  {
    this.streamFactory = StreamFactory;
    this.sizeFactory = SizeFactory;
    this.basePath = CopyOf.basePath;
    this.disposableExtra = DisposableExtra;
    this.volumeType = CopyOf.volumeType;
    this.svodLayout = CopyOf.svodLayout;
    this.contentFileCount = CopyOf.contentFileCount;
    this.dataBlockCount = CopyOf.dataBlockCount;
    this.startDataBlock = CopyOf.startDataBlock;
    this.svodBaseOffset = CopyOf.svodBaseOffset;
    this.title = CopyOf.title;
    this.IsValid = CopyOf.IsValid;
    this.ByteSize = CopyOf.ByteSize;
  }

  public static STFS LoadFromDir(string Path)
  {
    var ret = new STFS((path) => File.OpenRead(path), (path) => new FileInfo(path).Length, Path);

    return ret;
  }

  public static STFS LoadFromArchive(string Archive, string SubPath)
  {
    var archive = ZipFile.OpenRead(Archive);

    var ret = new STFS((path) => {
      var entry = archive.GetEntry(path);
      return entry?.Open();
    }, (path) => {
      var entry = archive.GetEntry(path);
      return entry?.Length;
    }, SubPath, archive);

    return ret;
  }

  public static STFS Invalid => new STFS((_) => null, (_) => null, "");

  public async Task ValidateAsync(CancellationToken ct = default)
  {
    if (IsValid)
      return;

    if (!await ReadAndValidateHeaderAsync(ct ))
      return;
    ct.ThrowIfCancellationRequested();

    switch (volumeType)
    {
    case ContentVolumeType.SVOD:
      if (!await ReadAndValidateSVODAsync(ct))
        return;
      break;
    }

    IsValid = true;
  }

  public void Dispose()
  {
    disposableExtra?.Dispose();
    disposableExtra = null;
  }

  async Task<bool> ReadAndValidateHeaderAsync(CancellationToken ct)
  {
    Debug.WriteLine($"Loading STFS header file {basePath}...");
    using var mainFile = streamFactory.Invoke(basePath);

    if (mainFile == null)
    {
      Debug.WriteLine("Failed to open STFS header file");
      return false;
    }

    // Values are big endian by default

    // STFS Content header
    // uint32 magic
    // uint8[0x228] signature
    // uint8[0x10 * 0x10] licenses
    // uint8[0x14] content_id
    // uint32 header_size
    var contentHeader = new byte[0x344];
    await mainFile.ReadExactlyAsync(contentHeader, 0, 0x344, ct);

    // STFS Metadata header
    // uint32 content_type
    // uint32 metadata_version
    // uint64 content_size
    // uint8[0x18] xex2_opt_execution_info
    // uint8[5] console_id
    // uint64 profile_id
    // uint8[0x24] volume_descriptor
    // uint32 data_file_count
    // uint64 data_file_size
    // uint32 volume_type
    // uint64 online_creator
    // uint32 category
    // uint8[0x20] ---
    // uint8[0x24] metadata_v2
    // uint8[0x14] device_id
    // uint16[9 * 128] display_names
    // uint16[9 * 128] descriptions
    // uint16[64] publisher
  // uint16[64] title_name
    // uint8 flags
    // uint32 thumbnail_size
    // uint32 title_thumbnail_size
    // uint8[0x3d00] thumbnail
    // uint16[3*128] display_names_ex
    // uint8[0x3d00] title_thumbnail
    // uint16[3*128] descriptions_ex
    var contentMetadata = new byte[0x93D6];
    await mainFile.ReadExactlyAsync(contentMetadata, 0, 0x93D6, ct);

    using (var headerStream = new MemoryStream(contentHeader))
    using (var headerReader = new BigEndianBinaryReader(headerStream))
    {
      var contentPackageType = (ContentPackageType)headerReader.ReadUInt32();
      Debug.WriteLine($"Read package type as {contentPackageType}");
      if (!Enum.IsDefined<ContentPackageType>(contentPackageType))
      {
        Debug.WriteLine("Failed to find a valid STFS header");
        return false;
      }
    }

    using (var metadataStream = new MemoryStream(contentMetadata))
    using (var metadataReader = new BigEndianBinaryReader(metadataStream))
    {
      metadataStream.Seek(4, SeekOrigin.Current);
      var metadataVersion = metadataReader.ReadUInt32();
      Debug.WriteLine($"Package metadata is version {metadataVersion}");

      var volumeDescriptor = new byte[0x24];
      metadataStream.Seek(45, SeekOrigin.Current);
      await metadataStream.ReadExactlyAsync(volumeDescriptor, 0, 0x24, ct);

      contentFileCount = (int)metadataReader.ReadUInt32();
      Debug.WriteLine($"Read content file count as {contentFileCount}");
      ByteSize = metadataReader.ReadUInt64();
      Debug.WriteLine($"Read content file size as {ByteSize}");

      volumeType = (ContentVolumeType)metadataReader.ReadUInt32();
      Debug.WriteLine($"Read volume type as {volumeType}");
      if (!Enum.IsDefined<ContentVolumeType>(volumeType))
      {
        Debug.WriteLine($"Failed to parse volume type {volumeType:X}");
        return false;
      }

      if (contentFileCount == 1 && volumeType == ContentVolumeType.SVOD)
      {
        Debug.WriteLine($"Is a single-file SVOD container");
        svodLayout = SVODLayout.SingleFile;
      }

      metadataStream.Seek(4941, SeekOrigin.Begin);
      title = await metadataStream.ReadWStringAsync(64);
      Debug.WriteLine($"Read title name as '{title}'");

      if (string.IsNullOrEmpty(title))
      {
        Debug.WriteLine("Failed to parse title name");
        return false;
      }

      if (volumeType == ContentVolumeType.SVOD)
      {
        // uint8 descriptor length
        // uint8 block cache element count
        // uint8 worker thread processor
        // uint8 worker thread priority
        // uint8[0x14] first fragment hash entry
        // uint8 features
        // uint24le num data blocks
        // uint24le start data block
        // uint8[5] padding
        using var svodStream = new MemoryStream(volumeDescriptor);
        using var svodLEReader = new BinaryReader(svodStream);

        svodStream.Seek(24, SeekOrigin.Begin);
        var flags = svodStream.ReadByte();
        Debug.WriteLine($"Read SVOD flags as {flags:X}");

        if ((flags & (1<<6)) != 0)
        {
          Debug.WriteLine("Uses enhanced GDF layout");
          svodLayout = SVODLayout.EGDF;
        }
        
        dataBlockCount = (int)svodLEReader.ReadUInt24();
        Debug.WriteLine($"Read datablock count as {dataBlockCount}");

        startDataBlock = (int)svodLEReader.ReadUInt24();
        Debug.WriteLine($"Read start datablock as {startDataBlock}");
      }
      else if (volumeType == ContentVolumeType.STFS)
      {
        // uint8 descriptor length
        // uint8 version
        // uint8 flags
        // uint16le file table block count
        // uint24le file table block number
        // uint8[0x14] top table hash
        // uint32 total block count
        // uint32 free block count
        
        using var stfsStream = new MemoryStream(volumeDescriptor);
        using var stfsBEReader = new BigEndianBinaryReader(stfsStream);
        using var stfsLEReader = new BinaryReader(stfsStream);

        stfsStream.Seek(2, SeekOrigin.Begin);

        var flags = stfsStream.ReadByte();
        Debug.WriteLine($"Read STFS flags as {flags:X}");

        stfsStream.Seek(28, SeekOrigin.Begin);

        dataBlockCount = (int)stfsLEReader.ReadUInt32();
        Debug.WriteLine($"Read datablock count as {dataBlockCount}");
      }
    }

    RecalculateByteSize();
    return true;
  }

  async Task<bool> ReadAndValidateSVODAsync(CancellationToken ct)
  {
    var initialFile = Path.Join($"{basePath}.data", "Data0000");
    if (volumeType == ContentVolumeType.STFS || svodLayout == SVODLayout.SingleFile)
      initialFile = basePath;

    Debug.WriteLine($"Loading SVOD data file {initialFile}...");
    using var mainFile = streamFactory.Invoke(initialFile);

    if (mainFile == null)
    {
      Debug.WriteLine("Failed to open SVOD data file");
      return false;
    }

    var xgdMagicBuf = new byte[20];
    var xsfMagicBuf = new byte[3];
    await mainFile.SeekForwardsAsync(0x2000, ct);
    if (svodLayout == SVODLayout.EGDF)
    {
      await mainFile.ReadExactlyAsync(xgdMagicBuf, 0, 20, ct);
      if (System.Text.Encoding.ASCII.GetString(xgdMagicBuf) != XGD.Magic)
      {
        Debug.WriteLine("Unable to find XGD magic at 0x2000 as required for EGDF");
        return false;
      }
    }
    else
    {
      await mainFile.ReadExactlyAsync(xsfMagicBuf, 0, 3, ct);
      if (System.Text.Encoding.ASCII.GetString(xsfMagicBuf) == XSF.Magic)
      {
        var toSeek = 0x10000-3;
        await mainFile.SeekForwardsAsync(toSeek, ct);
        await mainFile.ReadExactlyAsync(xgdMagicBuf, 0, 20, ct);
        if (System.Text.Encoding.ASCII.GetString(xgdMagicBuf) != XGD.Magic)
        {
          Debug.WriteLine("Failed to find XGD magic at 0x12000 as required for XSF");
          return false;
        }

        svodLayout = SVODLayout.XSF;
      }
      else
      {
        await mainFile.SeekForwardsAsync(0xb000, ct);
        await mainFile.ReadExactlyAsync(xgdMagicBuf, 0, 20, ct);
        if (System.Text.Encoding.ASCII.GetString(xgdMagicBuf) == XGD.Magic)
          svodLayout = SVODLayout.SingleFile;
        else
        {
          Debug.WriteLine("Failed to find XGD magic at 0xD000 as required for single-file SVOD");
          svodLayout = SVODLayout.Unknown;
        }
      }
    }

    return true;
  }

  const long HASH_BLOCK_SIZE = 0x1000;
  const long BLOCKS_PER_L0_HASH = 0x198;
  const long HASHES_PER_L1_HASH = 0xA1C4;
  const long BLOCKS_PER_FILE = 0x14388;
  const long MAX_FILE_SIZE = 0xA290000;

  void RecalculateByteSize()
  {
    // Recalculate size, removing hash block sizes
    switch (volumeType)
    {
      case ContentVolumeType.SVOD:
        if (svodLayout != SVODLayout.SingleFile)
        {
          ulong newByteSize = 0;
          // All full-size files
          newByteSize = (ulong)((contentFileCount - 1) * BLOCKS_PER_FILE * BLOCK_SIZE);

          // Final partial-size file
          var finalPartLen = sizeFactory($"{basePath}.data/Data{contentFileCount - 1:D4}");
          if (finalPartLen == null)
          {
            Debug.WriteLine("Unable to find size of final SVOD data container");
            return;
          }

          var containers = finalPartLen.Value / (BLOCKS_PER_L0_HASH * BLOCK_SIZE + HASH_BLOCK_SIZE);
          var containerSize = (containers + 2) * HASH_BLOCK_SIZE;
          newByteSize += (ulong)(finalPartLen.Value - containerSize);

          Debug.WriteLine($"SVOD size recalculated from {ByteSize} => {newByteSize}");
          ByteSize = newByteSize;
        }
        else
        {
          Debug.WriteLine("TODO: Single-file SVOD");
          // For single-file SVOD, just remove STFS header and hash tables
          ByteSize -= 0xC000;
          var containers = ByteSize / (BLOCKS_PER_L0_HASH * BLOCK_SIZE + HASH_BLOCK_SIZE);
          var containerSize = (containers + 2) * HASH_BLOCK_SIZE;
          var newByteSize = (ulong)(ByteSize - containerSize);

          Debug.WriteLine($"Recalculating STFS size from {ByteSize} => {newByteSize}");
          ByteSize = newByteSize;
        }
        break;
      case ContentVolumeType.STFS:
        {
          if (ByteSize == 0)
          {
            Debug.WriteLine("Failed to read byte size from STFS header, using file size");
            ByteSize = (ulong)(sizeFactory.Invoke(basePath) ?? 0xD000);
          }
          var containers = ByteSize / (BLOCKS_PER_L0_HASH * BLOCK_SIZE + HASH_BLOCK_SIZE);
          var containerSize = (containers + 2) * HASH_BLOCK_SIZE;
          var newByteSize = (ulong)(ByteSize - containerSize);

          Debug.WriteLine($"Recalculating STFS size from {ByteSize} => {newByteSize}");
          ByteSize = newByteSize;
        } break;
    }
  }

  struct BlockOffset
  {
    public ushort File;
    public long BlockAddress;
    public ushort OffsetInBlock;
    public ushort BlockRemaining;

    public override string ToString()
      => $"[Data{File:D4}@{BlockAddress}, for {OffsetInBlock}-{OffsetInBlock+BlockRemaining}]";
  }

  BlockOffset GetBlockMap(long position)
  {
    var BLOCK_OFFSET = startDataBlock;

    long block = position / BLOCK_SIZE;
    long blockPosition = position % BLOCK_SIZE;
    long trueBlock = block - BLOCK_OFFSET*2;

    if (svodLayout == SVODLayout.EGDF)
      trueBlock += 0x2;

    long fileBlock = trueBlock % BLOCKS_PER_FILE;
    long fileIndex = trueBlock / BLOCKS_PER_FILE;
    long offset = 0;

    long level0Tables = (fileBlock / BLOCKS_PER_L0_HASH) + 1;
    offset += level0Tables * HASH_BLOCK_SIZE;

    long level1Tables = (level0Tables / HASHES_PER_L1_HASH) + 1;
    offset += level1Tables * HASH_BLOCK_SIZE;

    if (svodLayout == SVODLayout.SingleFile)
      offset += svodBaseOffset;

    long blockAddress = fileBlock * BLOCK_SIZE + offset;

    if (blockAddress >= MAX_FILE_SIZE)
    {
      fileIndex++;
      blockAddress %= MAX_FILE_SIZE;
      blockAddress += 0x2000;
    }

    return new BlockOffset {
      File = (ushort)fileIndex,
      BlockAddress = blockAddress,
      OffsetInBlock = (ushort)blockPosition,
      BlockRemaining = (ushort)(BLOCK_SIZE - blockPosition)
    };
  }

  public async Task<long> ReadBytesAsync(byte[] buffer, long offset, long length, CancellationToken ct = default)
    => volumeType switch {
      ContentVolumeType.SVOD => await ReadSVODBytesAsync(buffer, offset, length, ct),
      ContentVolumeType.STFS => throw new NotImplementedException("TODO: STFS"),
      _ => throw new ArgumentOutOfRangeException("Not a valid container type")
    };

  ushort? activeFile;
  Stream? activeStream;
  long activeStreamPosition = 0;

  async Task<long> ReadSVODBytesAsync(byte[] buffer, long offset, long length, CancellationToken ct = default)
  {
    if (svodLayout == SVODLayout.SingleFile)
      throw new NotImplementedException("TODO: Single-file SVOD");

    if (offset % BLOCK_SIZE == 0 && length == BLOCK_SIZE)
    {
      await ReadSVODBlockAsync(buffer, GetBlockMap(offset), ct);
      return BLOCK_SIZE;
    }

    // Fallback for non-aligned reads, still reads full block and then extracts sub-read

    long totalRead = 0;
    long leftToRead = length;
    long readingOffset = offset;
    long writingOffset = 0;

    var blockBuf = new byte[BLOCK_SIZE];
    while (leftToRead > 0)
    {
      var block = GetBlockMap(readingOffset);
      await ReadSVODBlockAsync(blockBuf, block, ct);

      var clampedLeft = Math.Min(leftToRead, block.BlockRemaining);
      // Debug.WriteLine($"ReadBytes({offset}, {length}): Copying from block {block.OffsetInBlock}+{block.BlockRemaining} into {writingOffset}+{clampedLeft}");
      new ReadOnlySpan<byte>(blockBuf, block.OffsetInBlock, block.BlockRemaining)
        .CopyTo(new Span<byte>(buffer, (int)writingOffset, (int)clampedLeft));

      leftToRead -= clampedLeft;
      readingOffset += block.BlockRemaining;
      writingOffset += clampedLeft;
      totalRead += clampedLeft;
    }

    return totalRead;
  }

  [MemberNotNull("activeStream")]
  async Task PrepareSVODContainer(BlockOffset block, CancellationToken ct)
  {
    if (activeFile == block.File && activeStream != null && !activeStream.CanSeek && activeStreamPosition > block.BlockAddress)
    {
      Debug.WriteLine($"Resetting container link, as reverse seek is impossible");

      activeStream.Dispose();
      activeStream = null;
      activeFile = null;
      activeStreamPosition = 0;
    }

    if (block.File != activeFile)
    {
      if (activeStream != null)
        await activeStream.DisposeAsync();

      activeFile = block.File;
      var container = $"{basePath}.data/Data{block.File:D4}";

      Debug.WriteLine($"Switching to container {container}");
      activeStream = streamFactory.Invoke(container);
      activeStreamPosition = 0;
    }

    if (activeStream == null)
      throw new ApplicationException($"Failed to activate stream for container Data{block.File:D4}");

    if (block.BlockAddress >= activeStreamPosition)
    {
      var diff = block.BlockAddress - activeStreamPosition;
      var seeked = await activeStream.SeekForwardsAsync((long)diff, ct);
      activeStreamPosition += seeked;
    }
    else if (activeStream.CanSeek)
    {
      activeStream.Seek((long)block.BlockAddress, SeekOrigin.Begin);
      activeStreamPosition = block.BlockAddress;
    }
    else
      throw new ApplicationException("Attempted reverse seek on unseekable stream");

    if (activeStream.CanSeek && activeStreamPosition != activeStream.Position)
      throw new ApplicationException($"Stream position is not valid, expected {activeStreamPosition}, is {activeStream.Position}");
  }

  async Task ReadSVODBlockAsync(byte[] buffer, BlockOffset block, CancellationToken ct = default)
  {
    // Debug.WriteLine($"ReadBlock({block})");
    await PrepareSVODContainer(block, ct);

    var blockLenRemaining = BLOCK_SIZE;
    var blockBufOffset = 0;
    while (blockLenRemaining > 0)
    {
      var read = await activeStream.ReadAsync(buffer, blockBufOffset, (int)blockLenRemaining, ct);
      if (read == 0)
      {
        Debug.WriteLine($"readBlock({block}) - remaining {blockBufOffset}-{blockBufOffset+blockLenRemaining} => 0");
        break;
      }

      blockLenRemaining -= read;
      blockBufOffset += read;
      activeStreamPosition += read;
    }
  }
}
