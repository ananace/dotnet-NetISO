namespace NetISO.Protocol;

public enum Command
{
  Ping         = 0,
  GetIsoSize   = 1,
  HasType1File = 2,
  ReadData     = 3,
  GetIsoName   = 4,
  MountIso     = 5
}
