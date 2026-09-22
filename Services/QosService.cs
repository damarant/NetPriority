using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using NetPriority.Models;

namespace NetPriority.Services;

public sealed class QosService
{
    // ============================================================
    // PRIVILEGE CHECK
    // ============================================================

    public bool IsRunningAsAdministrator()
    {
        using WindowsIdentity identity =
            WindowsIdentity.GetCurrent();

        WindowsPrincipal principal =
            new WindowsPrincipal(identity);

        return principal.IsInRole(
            WindowsBuiltInRole.Administrator);
    }

    // ============================================================
    // READ ALL POLICIES
    // ============================================================

    public async Task<IReadOnlyList<QosRule>> GetPoliciesAsync()
    {
        const string script = """
$ErrorActionPreference = 'Stop'

$policies = @(Get-NetQosPolicy)

if ($policies.Count -eq 0) {
    Write-Output '[]'
    return
}

$policies |
    Select-Object `
        Name,
        Owner,
        NetworkProfile,
        Precedence,
        AppPathName,
        JobObject,
        DSCPValue |
    ConvertTo-Json -Compress
""";

        string output = await RunPowerShellAsync(script);

        if (string.IsNullOrWhiteSpace(output))
        {
            return [];
        }

        output = output.Trim();

        if (output == "[]")
        {
            return [];
        }

        try
        {
            using JsonDocument document =
                JsonDocument.Parse(output);

            JsonElement root =
                document.RootElement;

            // ----------------------------------------------------
            // MULTIPLE POLICIES
            // ----------------------------------------------------

            if (root.ValueKind == JsonValueKind.Array)
            {
                List<QosRule> rules = [];

                foreach (JsonElement element
                         in root.EnumerateArray())
                {
                    rules.Add(ParseRule(element));
                }

                return rules;
            }

            // ----------------------------------------------------
            // SINGLE POLICY
            // ----------------------------------------------------

            if (root.ValueKind == JsonValueKind.Object)
            {
                return
                [
                    ParseRule(root)
                ];
            }

            return [];
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                "Error while reading QoS rules."
                + Environment.NewLine
                + Environment.NewLine
                + "Response received from PowerShell:"
                + Environment.NewLine
                + output,
                ex);
        }
    }

    // ============================================================
    // READ A SPECIFIC POLICY
    // ============================================================

    private async Task<QosRule?> GetPolicyByNameAsync(
        string ruleName)
    {
        string escapedName =
            EscapePowerShellString(ruleName);

        const string template = """
$ErrorActionPreference = 'Stop'

$policy = Get-NetQosPolicy `
    -Name '__RULE_NAME__' `
    -ErrorAction SilentlyContinue

if ($null -eq $policy) {
    Write-Output 'NO_POLICY'
    return
}

$policy |
    Select-Object `
        Name,
        Owner,
        NetworkProfile,
        Precedence,
        AppPathName,
        JobObject,
        DSCPValue |
    ConvertTo-Json -Compress
""";

        string script =
            template.Replace(
                "__RULE_NAME__",
                escapedName);

        string output =
            await RunPowerShellAsync(script);

        output = output.Trim();

        if (string.IsNullOrWhiteSpace(output) ||
            output == "NO_POLICY")
        {
            return null;
        }

        try
        {
            using JsonDocument document =
                JsonDocument.Parse(output);

            JsonElement root =
                document.RootElement;

            if (root.ValueKind !=
                JsonValueKind.Object)
            {
                return null;
            }

            return ParseRule(root);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                "Unable to parse the response "
                + "from Get-NetQosPolicy."
                + Environment.NewLine
                + Environment.NewLine
                + "Response received:"
                + Environment.NewLine
                + output,
                ex);
        }
    }

    // ============================================================
    // CREATE POLICY
    // ============================================================

    public async Task CreatePolicyAsync(
        string ruleName,
        string applicationPath,
        int dscpValue)
    {
        if (!IsRunningAsAdministrator())
        {
            throw new UnauthorizedAccessException(
                "NetPriority must be run as administrator.");
        }

        ValidateRuleName(ruleName);

        ValidateApplication(applicationPath);

        ValidateDscp(dscpValue);

        // --------------------------------------------------------
        // Check if the policy already exists
        // --------------------------------------------------------

        QosRule? existing =
            await GetPolicyByNameAsync(ruleName);

        if (existing != null)
        {
            throw new InvalidOperationException(
                $"QoS rule '{ruleName}' already exists.");
        }

        string escapedName =
            EscapePowerShellString(ruleName);

        string escapedApplication =
            EscapePowerShellString(applicationPath);

        const string template = """
$ErrorActionPreference = 'Stop'

New-NetQosPolicy `
    -Name '__RULE_NAME__' `
    -AppPathNameMatchCondition '__APPLICATION__' `
    -DSCPAction __DSCP__ `
    -NetworkProfile All `
    -Confirm:$false
""";

        string script =
            template
                .Replace(
                    "__RULE_NAME__",
                    escapedName)
                .Replace(
                    "__APPLICATION__",
                    escapedApplication)
                .Replace(
                    "__DSCP__",
                    dscpValue.ToString());

        await RunPowerShellAsync(script);

        // --------------------------------------------------------
        // REAL VERIFICATION AFTER CREATION
        // --------------------------------------------------------

        QosRule? created =
            await GetPolicyByNameAsync(ruleName);

        if (created == null)
        {
            throw new InvalidOperationException(
                $"Windows does not return the policy '{ruleName}' "
                + "after creation.");
        }

        // --------------------------------------------------------
        // PATH VERIFICATION
        // --------------------------------------------------------

        string expectedPath =
            NormalizePath(applicationPath);

        string actualPath =
            NormalizePath(created.ApplicationPath);

        if (!string.IsNullOrWhiteSpace(actualPath) &&
            !string.Equals(
                actualPath,
                expectedPath,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The policy was created, but the application "
                + "path does not match."
                + Environment.NewLine
                + "Requested: "
                + applicationPath
                + Environment.NewLine
                + "Returned: "
                + created.ApplicationPath);
        }

        // --------------------------------------------------------
        // DSCP VERIFICATION
        // --------------------------------------------------------

        if (created.DscpValue != dscpValue)
        {
            throw new InvalidOperationException(
                "The policy was created, but the DSCP value "
                + $"returned by Windows is {created.DscpValue} "
                + $"instead of {dscpValue}.");
        }
    }

    // ============================================================
    // REMOVE POLICY
    // ============================================================

    public async Task RemovePolicyAsync(
        string ruleName)
    {
        if (!IsRunningAsAdministrator())
        {
            throw new UnauthorizedAccessException(
                "NetPriority must be run as administrator.");
        }

        if (string.IsNullOrWhiteSpace(ruleName))
        {
            throw new ArgumentException(
                "The policy name is not valid.",
                nameof(ruleName));
        }

        // --------------------------------------------------------
        // Existence check
        // --------------------------------------------------------

        QosRule? existing =
            await GetPolicyByNameAsync(ruleName);

        if (existing == null)
        {
            throw new InvalidOperationException(
                $"Policy '{ruleName}' does not exist.");
        }

        string escapedName =
            EscapePowerShellString(ruleName);

        const string template = """
$ErrorActionPreference = 'Stop'

Remove-NetQosPolicy `
    -Name '__RULE_NAME__' `
    -Confirm:$false
""";

        string script =
            template.Replace(
                "__RULE_NAME__",
                escapedName);

        await RunPowerShellAsync(script);

        // --------------------------------------------------------
        // REAL VERIFICATION OF THE REMOVAL
        // --------------------------------------------------------

        QosRule? afterRemoval =
            await GetPolicyByNameAsync(ruleName);

        if (afterRemoval != null)
        {
            throw new InvalidOperationException(
                $"Policy '{ruleName}' is still present.");
        }
    }

    // ============================================================
    // JSON -> QosRule CONVERSION
    // ============================================================

    private static QosRule ParseRule(
        JsonElement element)
    {
        string name =
            GetString(
                element,
                "Name");

        return new QosRule
        {
            Name = name,

            ApplicationPath =
                GetString(
                    element,
                    "AppPathName"),

            DscpValue =
                GetInt(
                    element,
                    "DSCPValue"),

            Description =
                string.Empty,

            Owner =
                GetString(
                    element,
                    "Owner"),

            Precedence =
                GetUInt(
                    element,
                    "Precedence"),

            IsManagedByNetPriority =
                name.StartsWith(
                    "NetPriority",
                    StringComparison.OrdinalIgnoreCase)
        };
    }

    // ============================================================
    // READ JSON STRING
    // ============================================================

    private static string GetString(
        JsonElement element,
        string propertyName)
    {
        if (!element.TryGetProperty(
                propertyName,
                out JsonElement value))
        {
            return string.Empty;
        }

        if (value.ValueKind ==
            JsonValueKind.Null)
        {
            return string.Empty;
        }

        return value.ToString();
    }

    // ============================================================
    // READ JSON INT
    // ============================================================

    private static int? GetInt(
        JsonElement element,
        string propertyName)
    {
        if (!element.TryGetProperty(
                propertyName,
                out JsonElement value))
        {
            return null;
        }

        if (value.ValueKind ==
            JsonValueKind.Number &&
            value.TryGetInt32(
                out int number))
        {
            return number;
        }

        if (int.TryParse(
                value.ToString(),
                out int parsed))
        {
            return parsed;
        }

        return null;
    }

    // ============================================================
    // READ JSON UINT
    // ============================================================

    private static uint? GetUInt(
        JsonElement element,
        string propertyName)
    {
        if (!element.TryGetProperty(
                propertyName,
                out JsonElement value))
        {
            return null;
        }

        if (value.ValueKind ==
            JsonValueKind.Number &&
            value.TryGetUInt32(
                out uint number))
        {
            return number;
        }

        if (uint.TryParse(
                value.ToString(),
                out uint parsed))
        {
            return parsed;
        }

        return null;
    }

    // ============================================================
    // NAME VALIDATION
    // ============================================================

    private static void ValidateRuleName(
        string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException(
                "Enter a name for the rule.");
        }

        if (name.Length > 100)
        {
            throw new ArgumentException(
                "The rule name is too long.");
        }
    }

    // ============================================================
    // APPLICATION VALIDATION
    // ============================================================

    private static void ValidateApplication(
        string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException(
                "The application path is empty.");
        }

        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                "The selected executable file does not exist.",
                path);
        }

        if (!string.Equals(
                Path.GetExtension(path),
                ".exe",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "Only a .exe file can be selected.");
        }
    }

    // ============================================================
    // DSCP VALIDATION
    // ============================================================

    private static void ValidateDscp(
        int dscpValue)
    {
        if (dscpValue < 0 ||
            dscpValue > 63)
        {
            throw new ArgumentOutOfRangeException(
                nameof(dscpValue),
                "The DSCP value must be between 0 and 63.");
        }
    }

    // ============================================================
    // PATH NORMALIZATION
    // ============================================================

    private static string NormalizePath(
        string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        return path
            .Trim()
            .Trim('"')
            .Replace(
                '/',
                '\\');
    }

    // ============================================================
    // POWERSHELL STRING ESCAPING
    // ============================================================

    private static string EscapePowerShellString(
        string value)
    {
        return value.Replace(
            "'",
            "''");
    }

    // ============================================================
    // POWERSHELL EXECUTION
    //
    // We use -EncodedCommand.
    //
    // This avoids issues with:
    // - $
    // - {}
    // - pipe
    // - quotes
    // - multiline scripts
    // - StandardInput
    // ============================================================

    private static async Task<string>
        RunPowerShellAsync(
            string script)
    {
        byte[] scriptBytes =
            Encoding.Unicode.GetBytes(script);

        string encodedCommand =
            Convert.ToBase64String(
                scriptBytes);

        ProcessStartInfo startInfo =
            new ProcessStartInfo
            {
                FileName =
                    "powershell.exe",

                Arguments =
                    "-NoProfile "
                    + "-NonInteractive "
                    + "-EncodedCommand "
                    + encodedCommand,

                RedirectStandardOutput =
                    true,

                RedirectStandardError =
                    true,

                UseShellExecute =
                    false,

                CreateNoWindow =
                    true,

                StandardOutputEncoding =
                    Encoding.UTF8,

                StandardErrorEncoding =
                    Encoding.UTF8
            };

        using Process process =
            new Process
            {
                StartInfo =
                    startInfo
            };

        process.Start();

        Task<string> outputTask =
            process.StandardOutput
                .ReadToEndAsync();

        Task<string> errorTask =
            process.StandardError
                .ReadToEndAsync();

        await Task.WhenAll(
            outputTask,
            errorTask);

        await process.WaitForExitAsync();

        string output =
            await outputTask;

        string error =
            await errorTask;

        if (process.ExitCode != 0)
        {
            string message =
                string.IsNullOrWhiteSpace(error)
                    ? "PowerShell returned an error."
                    : error.Trim();

            throw new InvalidOperationException(
                message);
        }

        return output.Trim();
    }
}