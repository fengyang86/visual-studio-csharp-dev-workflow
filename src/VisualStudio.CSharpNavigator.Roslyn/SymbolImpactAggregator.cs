using System;
using System.Collections.Generic;
using System.Linq;
using VisualStudio.CSharpNavigator.Protocol;

namespace VisualStudio.CSharpNavigator.Roslyn;

public sealed class SymbolImpactAggregationResult
{
    public SymbolImpactSummary Summary { get; set; } = new();

    public IReadOnlyList<string> Diagnostics { get; set; } = Array.Empty<string>();

    public bool IsPartial { get; set; }
}

public sealed class SymbolImpactAggregationOptions
{
    public int MaxReferences { get; set; }

    public bool IsReferenceCollectionPartial { get; set; }

    public int MaxDepth { get; set; } = 1;

    public int MaxProjects { get; set; } = 20;

    public int MaxFiles { get; set; } = 20;

    public int MaxContainingTypes { get; set; } = 20;
}

public sealed class SymbolImpactSite
{
    public string ProjectName { get; set; } = string.Empty;

    public string FilePath { get; set; } = string.Empty;

    public string ContainingType { get; set; } = string.Empty;

    public ReferenceRole Role { get; set; }

    public int Depth { get; set; } = 1;
}

public static class SymbolImpactAggregator
{
    private const string GlobalContainingType = "<global>";

    public static SymbolImpactAggregationResult Aggregate(
        SymbolDescriptor targetSymbol,
        IReadOnlyList<SymbolImpactSite> sites,
        SymbolImpactAggregationOptions options)
    {
        var normalizedSites = sites
            .Select(site => new SymbolImpactSite
            {
                ProjectName = site.ProjectName ?? string.Empty,
                FilePath = site.FilePath ?? string.Empty,
                ContainingType = string.IsNullOrWhiteSpace(site.ContainingType) ? GlobalContainingType : site.ContainingType,
                Role = site.Role,
                Depth = site.Depth < 1 ? 1 : site.Depth,
            })
            .ToArray();

        var diagnostics = new List<string>();
        var isPartial = false;
        var targetProjectName = targetSymbol.ProjectName ?? string.Empty;

        var projects = normalizedSites
            .GroupBy(site => site.ProjectName, StringComparer.OrdinalIgnoreCase)
            .Select(group => new SymbolImpactProjectSummary
            {
                ProjectName = group.Key,
                Count = group.Count(),
                IsTargetProject = string.Equals(group.Key, targetProjectName, StringComparison.OrdinalIgnoreCase),
            })
            .OrderByDescending(item => item.Count)
            .ThenBy(item => item.ProjectName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var files = normalizedSites
            .GroupBy(site => new FileGroupKey(site.ProjectName, site.FilePath))
            .Select(group => new SymbolImpactFileSummary
            {
                ProjectName = group.Key.ProjectName,
                FilePath = group.Key.FilePath,
                Count = group.Count(),
                RoleCounts = CreateRoleCounts(group.Select(item => item.Role)),
            })
            .OrderByDescending(item => item.Count)
            .ThenBy(item => item.ProjectName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.FilePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var containingTypes = normalizedSites
            .GroupBy(site => new ContainingTypeGroupKey(site.ProjectName, site.ContainingType))
            .Select(group => new SymbolImpactContainingTypeSummary
            {
                ProjectName = group.Key.ProjectName,
                ContainingType = group.Key.ContainingType,
                Count = group.Count(),
                RoleCounts = CreateRoleCounts(group.Select(item => item.Role)),
            })
            .OrderByDescending(item => item.Count)
            .ThenBy(item => item.ProjectName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.ContainingType, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var summary = new SymbolImpactSummary
        {
            Symbol = targetSymbol,
            MaxDepth = options.MaxDepth,
            TotalReferences = normalizedSites.Length,
            ProcessedReferenceCount = normalizedSites.Length,
            ReferenceCollectionLimit = options.MaxReferences,
            IsReferenceCollectionPartial = options.IsReferenceCollectionPartial,
            DirectReferenceCount = normalizedSites.Count(site => site.Depth == 1),
            TransitiveReferenceCount = normalizedSites.Count(site => site.Depth > 1),
            DistinctProjectCount = projects.Length,
            DistinctFileCount = files.Length,
            DistinctContainingTypeCount = containingTypes.Length,
            HasCrossProjectImpact = projects.Any(project => !project.IsTargetProject),
            RoleCounts = CreateRoleCounts(normalizedSites.Select(site => site.Role)),
            Projects = TakeWithDiagnostics(projects, options.MaxProjects, "Projects", diagnostics, ref isPartial),
            Files = TakeWithDiagnostics(files, options.MaxFiles, "Files", diagnostics, ref isPartial),
            ContainingTypes = TakeWithDiagnostics(containingTypes, options.MaxContainingTypes, "ContainingTypes", diagnostics, ref isPartial),
        };

        return new SymbolImpactAggregationResult
        {
            Summary = summary,
            Diagnostics = diagnostics,
            IsPartial = isPartial,
        };
    }

    private static IReadOnlyList<ReferenceRoleCount> CreateRoleCounts(IEnumerable<ReferenceRole> roles)
    {
        return roles
            .GroupBy(role => role)
            .Select(group => new ReferenceRoleCount
            {
                Role = group.Key,
                Count = group.Count(),
            })
            .OrderByDescending(item => item.Count)
            .ThenBy(item => item.Role)
            .ToArray();
    }

    private static IReadOnlyList<T> TakeWithDiagnostics<T>(
        IReadOnlyList<T> items,
        int maxCount,
        string label,
        List<string> diagnostics,
        ref bool isPartial)
    {
        if (items.Count <= maxCount)
        {
            return items;
        }

        diagnostics.Add($"{label} were truncated at {label switch
        {
            "Projects" => "MaxProjects",
            "Files" => "MaxFiles",
            _ => "MaxContainingTypes",
        }}={maxCount}.");
        isPartial = true;
        return items.Take(maxCount).ToArray();
    }

    private sealed class FileGroupKey : IEquatable<FileGroupKey>
    {
        public FileGroupKey(string projectName, string filePath)
        {
            ProjectName = projectName;
            FilePath = filePath;
        }

        public string ProjectName { get; }

        public string FilePath { get; }

        public bool Equals(FileGroupKey? other)
        {
            return other is not null
                && string.Equals(ProjectName, other.ProjectName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(FilePath, other.FilePath, StringComparison.OrdinalIgnoreCase);
        }

        public override bool Equals(object? obj) => Equals(obj as FileGroupKey);

        public override int GetHashCode()
        {
            return StringComparer.OrdinalIgnoreCase.GetHashCode(ProjectName)
                ^ StringComparer.OrdinalIgnoreCase.GetHashCode(FilePath);
        }
    }

    private sealed class ContainingTypeGroupKey : IEquatable<ContainingTypeGroupKey>
    {
        public ContainingTypeGroupKey(string projectName, string containingType)
        {
            ProjectName = projectName;
            ContainingType = containingType;
        }

        public string ProjectName { get; }

        public string ContainingType { get; }

        public bool Equals(ContainingTypeGroupKey? other)
        {
            return other is not null
                && string.Equals(ProjectName, other.ProjectName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(ContainingType, other.ContainingType, StringComparison.OrdinalIgnoreCase);
        }

        public override bool Equals(object? obj) => Equals(obj as ContainingTypeGroupKey);

        public override int GetHashCode()
        {
            return StringComparer.OrdinalIgnoreCase.GetHashCode(ProjectName)
                ^ StringComparer.OrdinalIgnoreCase.GetHashCode(ContainingType);
        }
    }
}
