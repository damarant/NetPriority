using System.Diagnostics;
using NetPriority.Models;

namespace NetPriority.Services;

public sealed class ProcessService
{
    public IReadOnlyList<ApplicationInfo> GetRunningApplications()
    {
        var applications = new List<ApplicationInfo>();

        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (process.HasExited)
                    continue;

                string? executablePath = null;

                try
                {
                    executablePath = process.MainModule?.FileName;
                }
                catch
                {
                    // Some Windows processes are protected
                    // and do not allow reading their path.
                }

                if (string.IsNullOrWhiteSpace(executablePath))
                    continue;

                applications.Add(new ApplicationInfo
                {
                    ProcessId = process.Id,
                    ProcessName = $"{process.ProcessName}.exe",
                    ExecutablePath = executablePath
                });
            }
            catch
            {
                // The process may have terminated
                // while we were reading it.
            }
            finally
            {
                process.Dispose();
            }
        }

        return applications
            .GroupBy(
                x => x.ExecutablePath,
                StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First())
            .OrderBy(x => x.ProcessName)
            .ToList();
    }
}