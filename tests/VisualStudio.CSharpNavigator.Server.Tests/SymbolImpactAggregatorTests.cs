using VisualStudio.CSharpNavigator.Protocol;
using VisualStudio.CSharpNavigator.Roslyn;

namespace VisualStudio.CSharpNavigator.Server.Tests;

public sealed class SymbolImpactAggregatorTests
{
    [Fact]
    public void Aggregate_GroupsByProjectFileRoleAndContainingType()
    {
        var target = new SymbolDescriptor
        {
            Name = "SetProps",
            ContainingType = "SampleWorkspace.Core.LcObject",
            ProjectName = "SampleWorkspace.Core",
            Kind = CodeSymbolKind.Method,
            Span = new SourceSpan
            {
                FilePath = @"D:\Samples\SampleWorkspace\src\SampleWorkspace.Core\LcObject.cs",
                StartLine = 199,
                StartColumn = 21,
                EndLine = 199,
                EndColumn = 29,
            },
        };

        var sites = new[]
        {
            new SymbolImpactSite
            {
                ProjectName = "SampleWorkspace.Core",
                FilePath = @"D:\Samples\SampleWorkspace\src\SampleWorkspace.Core\A.cs",
                ContainingType = "SampleWorkspace.Core.ExecutorA",
                Role = ReferenceRole.Invocation,
            },
            new SymbolImpactSite
            {
                ProjectName = "SampleWorkspace.Core",
                FilePath = @"D:\Samples\SampleWorkspace\src\SampleWorkspace.Core\A.cs",
                ContainingType = "SampleWorkspace.Core.ExecutorA",
                Role = ReferenceRole.Invocation,
            },
            new SymbolImpactSite
            {
                ProjectName = "SampleWorkspace.Drawing",
                FilePath = @"D:\Samples\SampleWorkspace\src\SampleWorkspace.Drawing\B.cs",
                ContainingType = "SampleWorkspace.Drawing.ExecutorB",
                Role = ReferenceRole.Read,
                Depth = 2,
            },
            new SymbolImpactSite
            {
                ProjectName = "SampleWorkspace.Drawing",
                FilePath = @"D:\Samples\SampleWorkspace\src\SampleWorkspace.Drawing\C.cs",
                ContainingType = "SampleWorkspace.Drawing.ExecutorC",
                Role = ReferenceRole.Write,
            },
        };

        var result = SymbolImpactAggregator.Aggregate(
            target,
            sites,
            new SymbolImpactAggregationOptions
            {
                MaxReferences = 100,
                MaxDepth = 2,
                MaxProjects = 10,
                MaxFiles = 10,
                MaxContainingTypes = 10,
            });

        Assert.False(result.IsPartial);
        Assert.Empty(result.Diagnostics);
        Assert.Equal(2, result.Summary.MaxDepth);
        Assert.Equal(4, result.Summary.TotalReferences);
        Assert.Equal(4, result.Summary.ProcessedReferenceCount);
        Assert.Equal(100, result.Summary.ReferenceCollectionLimit);
        Assert.False(result.Summary.IsReferenceCollectionPartial);
        Assert.Equal(3, result.Summary.DirectReferenceCount);
        Assert.Equal(1, result.Summary.TransitiveReferenceCount);
        Assert.Equal(2, result.Summary.DistinctProjectCount);
        Assert.Equal(3, result.Summary.DistinctFileCount);
        Assert.Equal(3, result.Summary.DistinctContainingTypeCount);
        Assert.True(result.Summary.HasCrossProjectImpact);

        Assert.Collection(
            result.Summary.RoleCounts,
            item =>
            {
                Assert.Equal(ReferenceRole.Invocation, item.Role);
                Assert.Equal(2, item.Count);
            },
            item =>
            {
                Assert.Equal(ReferenceRole.Read, item.Role);
                Assert.Equal(1, item.Count);
            },
            item =>
            {
                Assert.Equal(ReferenceRole.Write, item.Role);
                Assert.Equal(1, item.Count);
            });

        Assert.Collection(
            result.Summary.Projects,
            item =>
            {
                Assert.Equal("SampleWorkspace.Core", item.ProjectName);
                Assert.Equal(2, item.Count);
                Assert.True(item.IsTargetProject);
            },
            item =>
            {
                Assert.Equal("SampleWorkspace.Drawing", item.ProjectName);
                Assert.Equal(2, item.Count);
                Assert.False(item.IsTargetProject);
            });

        var firstFile = Assert.IsType<SymbolImpactFileSummary>(result.Summary.Files[0]);
        Assert.Equal(@"D:\Samples\SampleWorkspace\src\SampleWorkspace.Core\A.cs", firstFile.FilePath);
        Assert.Equal(2, firstFile.Count);
        Assert.Single(firstFile.RoleCounts);
        Assert.Equal(ReferenceRole.Invocation, firstFile.RoleCounts[0].Role);

        var firstContainingType = Assert.IsType<SymbolImpactContainingTypeSummary>(result.Summary.ContainingTypes[0]);
        Assert.Equal("SampleWorkspace.Core.ExecutorA", firstContainingType.ContainingType);
        Assert.Equal(2, firstContainingType.Count);
    }

    [Fact]
    public void Aggregate_WhenGroupCountExceedsLimit_ReturnsPartialWithDiagnostics()
    {
        var target = new SymbolDescriptor
        {
            Name = "Start",
            ContainingType = "SampleWorkspace.Core.Elements.LcLine",
            ProjectName = "SampleWorkspace.Core",
            Kind = CodeSymbolKind.Property,
        };

        var sites = new[]
        {
            new SymbolImpactSite
            {
                ProjectName = "SampleWorkspace.Core",
                FilePath = @"D:\Samples\SampleWorkspace\src\SampleWorkspace.Core\A.cs",
                ContainingType = "SampleWorkspace.Core.ExecutorA",
                Role = ReferenceRole.Read,
            },
            new SymbolImpactSite
            {
                ProjectName = "SampleWorkspace.Drawing",
                FilePath = @"D:\Samples\SampleWorkspace\src\SampleWorkspace.Drawing\B.cs",
                ContainingType = "SampleWorkspace.Drawing.ExecutorB",
                Role = ReferenceRole.Write,
            },
        };

        var result = SymbolImpactAggregator.Aggregate(
            target,
            sites,
            new SymbolImpactAggregationOptions
            {
                MaxReferences = 2,
                IsReferenceCollectionPartial = true,
                MaxProjects = 1,
                MaxFiles = 1,
                MaxContainingTypes = 1,
            });

        Assert.True(result.IsPartial);
        Assert.Equal(2, result.Summary.ProcessedReferenceCount);
        Assert.Equal(2, result.Summary.ReferenceCollectionLimit);
        Assert.True(result.Summary.IsReferenceCollectionPartial);
        Assert.Equal(3, result.Diagnostics.Count);
        Assert.Single(result.Summary.Projects);
        Assert.Single(result.Summary.Files);
        Assert.Single(result.Summary.ContainingTypes);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Contains("MaxProjects"));
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Contains("MaxFiles"));
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Contains("MaxContainingTypes"));
    }
}
