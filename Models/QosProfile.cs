namespace NetPriority.Models;

public sealed class QosProfile
{
    public string Name { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public int DscpValue { get; init; }
}
