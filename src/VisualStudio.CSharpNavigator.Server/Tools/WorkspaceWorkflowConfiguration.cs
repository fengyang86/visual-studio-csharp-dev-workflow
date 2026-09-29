using System.Text.Json;

namespace VisualStudio.CSharpNavigator.Server.Tools;

internal sealed class WorkspaceWorkflowConfiguration
{
    public DiagnosticsWorkflowConfiguration Diagnostics { get; set; } = new();
}

internal sealed class DiagnosticsWorkflowConfiguration
{
    public string[] NoisePathPatterns { get; set; } = Array.Empty<string>();
}

internal static class WorkspaceWorkflowConfigurationLoader
{
    private const string FileName = ".csharp-workflow.json";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static string[] LoadNoisePathPatterns(string? solutionPath, ICollection<string>? diagnostics = null)
    {
        if (string.IsNullOrWhiteSpace(solutionPath))
        {
            return Array.Empty<string>();
        }

        var directory = new FileInfo(solutionPath).Directory;
        for (var depth = 0; directory is not null && depth < 8; depth++, directory = directory.Parent)
        {
            var path = Path.Combine(directory.FullName, FileName);
            if (!File.Exists(path))
            {
                continue;
            }

            try
            {
                var json = File.ReadAllText(path);
                var configuration = JsonSerializer.Deserialize<WorkspaceWorkflowConfiguration>(json, JsonOptions);
                return configuration?.Diagnostics?.NoisePathPatterns
                    .Where(pattern => !string.IsNullOrWhiteSpace(pattern))
                    .Select(pattern => pattern.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(100)
                    .ToArray() ?? Array.Empty<string>();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                diagnostics?.Add($"WorkflowConfigurationReadFailed: {path}: {ex.Message}");
                return Array.Empty<string>();
            }
        }

        return Array.Empty<string>();
    }
}
