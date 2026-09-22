namespace NetPriority.Models;

public sealed class ApplicationInfo
{
    public int ProcessId { get; init; }

    public string ProcessName { get; init; } = string.Empty;

    public string ExecutablePath { get; init; } = string.Empty;
}
