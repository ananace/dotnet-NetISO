namespace NetISO.Extensions;

public static class IISOSourceExtensions
{
  public static long GetByteSize(this IISOSource ISO)
  {
    return (long)ISO.SectorCount * (long)ISO.SectorSize;
  }
}
