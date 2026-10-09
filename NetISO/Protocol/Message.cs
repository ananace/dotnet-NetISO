namespace NetISO.Protocol;

public struct Message
{
  public Command Command { get; init; }
  public UInt16 ISOIndex { get; init; }
  public UInt64 Offset { get; init; }
  public UInt32 Length { get; init; }

  public override string ToString()
  {
    if (!Enum.IsDefined<Command>(Command))
      return $"<Message Command=Unknown ({Command:X}) ISO={ISOIndex} Offset={Offset} Length={Length}>";

    return $"<Message Command={Command} ISO={ISOIndex} Offset={Offset} Length={Length}>";
  }
}
