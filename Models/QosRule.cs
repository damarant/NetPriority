namespace NetPriority.Models;

public sealed class QosRule
{
    public string Name { get; set; } =
        string.Empty;

    public string ApplicationPath { get; set; } =
        string.Empty;

    public int? DscpValue { get; set; }

    public string Description { get; set; } =
        string.Empty;

    public string Owner { get; set; } =
        string.Empty;

    public uint? Precedence { get; set; }

    public bool IsManagedByNetPriority { get; set; }
}
