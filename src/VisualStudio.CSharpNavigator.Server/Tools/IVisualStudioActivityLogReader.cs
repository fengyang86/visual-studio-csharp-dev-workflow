using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace VisualStudio.CSharpNavigator.Server.Tools;

public interface IVisualStudioActivityLogReader
{
    string[] ReadRecentIssues(int maxIssues = 5);
}

public sealed class VisualStudioActivityLogReader : IVisualStudioActivityLogReader
{
    public string[] ReadRecentIssues(int maxIssues = 5)
    {
        var diagnostics = new List<string>();
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrWhiteSpace(appData))
        {
            return new[] { "VisualStudioActivityLogUnavailable: ApplicationData folder is unavailable." };
        }

        var visualStudioRoot = Path.Combine(appData, "Microsoft", "VisualStudio");
        if (!Directory.Exists(visualStudioRoot))
        {
            return new[] { $"VisualStudioActivityLogUnavailable: Visual Studio roaming profile folder was not found: {visualStudioRoot}" };
        }

        FileInfo? latestLog;
        try
        {
            latestLog = new DirectoryInfo(visualStudioRoot)
                .EnumerateFiles("ActivityLog.xml", SearchOption.AllDirectories)
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .FirstOrDefault();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException or PathTooLongException)
        {
            return new[] { "VisualStudioActivityLogUnavailable: " + ex.Message };
        }

        if (latestLog is null)
        {
            return new[] { $"VisualStudioActivityLogUnavailable: ActivityLog.xml was not found under {visualStudioRoot}" };
        }

        diagnostics.Add($"VisualStudioActivityLogLatest: path={latestLog.FullName}; lastWriteUtc={latestLog.LastWriteTimeUtc:O}.");

        string[] issues;
        try
        {
            issues = ReadActivityLogIssues(latestLog.FullName, Math.Max(1, maxIssues));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            diagnostics.Add("VisualStudioActivityLogReadFailed: " + ex.Message);
            return diagnostics.ToArray();
        }

        if (issues.Length == 0)
        {
            diagnostics.Add("VisualStudioActivityLogIssueSummary: no recent package-load or bridge-related issue was found in the latest ActivityLog.xml.");
            return diagnostics.ToArray();
        }

        diagnostics.AddRange(issues.Select(issue => "VisualStudioActivityLogIssue: " + issue));
        return diagnostics.ToArray();
    }

    private static string[] ReadActivityLogIssues(string activityLogPath, int maxIssues)
    {
        var document = XDocument.Load(activityLogPath);
        return document
            .Descendants()
            .Where(element => string.Equals(element.Name.LocalName, "entry", StringComparison.OrdinalIgnoreCase))
            .Select(FormatActivityLogEntry)
            .Where(IsImportantActivityLogIssue)
            .Reverse()
            .Take(maxIssues)
            .Reverse()
            .ToArray();
    }

    private static string FormatActivityLogEntry(XElement entry)
    {
        static string Value(XElement parent, string name)
        {
            return parent
                .Elements()
                .FirstOrDefault(element => string.Equals(element.Name.LocalName, name, StringComparison.OrdinalIgnoreCase))
                ?.Value
                .Trim() ?? string.Empty;
        }

        var type = Value(entry, "type");
        var source = Value(entry, "source");
        var description = NormalizeActivityLogText(Value(entry, "description"));
        var path = Value(entry, "path");
        var parts = new[] { type, source, description, path }
            .Where(part => !string.IsNullOrWhiteSpace(part));
        return string.Join(" | ", parts);
    }

    private static bool IsImportantActivityLogIssue(string entry)
    {
        if (string.IsNullOrWhiteSpace(entry))
        {
            return false;
        }

        var importantTerms = new[]
        {
            "SetSite failed",
            "WindowManagementPackage",
            "CodeNavigatorPackage",
            "CSharpNavigator",
            "VisualStudio CSharpNavigator",
            "Visual Studio C# Dev Workflow",
            "1c2e2f5d",
            "UriFormatException",
            "MS.Internal.FontCache.Util",
            "Package Load Failure",
        };
        return importantTerms.Any(term => entry.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0);
    }

    private static string NormalizeActivityLogText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var normalized = Regex.Replace(text, @"\s+", " ").Trim();
        return normalized.Length <= 500
            ? normalized
            : normalized[..500] + "...";
    }
}
