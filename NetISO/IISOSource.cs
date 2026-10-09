namespace NetISO;

public interface IISOSource : ICloneable
{
  public string DisplayName { get; }
  public bool IsValid { get; }

  public uint SectorCount { get; }
  public uint SectorSize { get; }

  public Task MountAsync();
  public Task UnmountAsync();

  public Task ReadAsync(byte[] buf, ulong offset, uint length, CancellationToken cancellationToken = default);
}
