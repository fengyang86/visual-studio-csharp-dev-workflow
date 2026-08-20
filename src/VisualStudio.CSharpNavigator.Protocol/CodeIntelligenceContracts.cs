using System;
using System.Collections.Generic;

namespace VisualStudio.CSharpNavigator.Protocol;

public enum CodeSymbolKind
{
    Unknown = 0,
    Namespace = 1,
    Type = 2,
    Method = 3,
    Property = 4,
    Field = 5,
    Event = 6,
    Parameter = 7,
    Local = 8,
}

public enum ReferenceRole
{
    Unknown = 0,
    Definition = 1,
    Declaration = 2,
    Read = 3,
    Write = 4,
    Invocation = 5,
    Implementation = 6,
    Override = 7,
}

public enum CallGraphEdgeKind
{
    Unknown = 0,
    Invocation = 1,
    ObjectCreation = 2,
    PropertyRead = 3,
    PropertyWrite = 4,
    EventReference = 5,
    FieldRead = 6,
    FieldWrite = 7,
}

public enum ProjectGraphEdgeKind
{
    Unknown = 0,
    ProjectReference = 1,
}

public sealed class SymbolDescriptor
{
    public SymbolKey? Key { get; set; }

    public string Name { get; set; } = string.Empty;

    public string ContainingType { get; set; } = string.Empty;

    public string ContainingNamespace { get; set; } = string.Empty;

    public string ProjectName { get; set; } = string.Empty;

    public CodeSymbolKind Kind { get; set; }

    public SourceSpan? Span { get; set; }
}

public sealed class SymbolSearchRequest : IVisualStudioBridgeTargetedRequest
{
    public VisualStudioBridgeTarget? Target { get; set; }

    public string QueryText { get; set; } = string.Empty;

    public int MaxResults { get; set; } = 50;

    public bool IncludeGeneratedCode { get; set; }
}

public sealed class SymbolReferenceRequest : IVisualStudioBridgeTargetedRequest
{
    public VisualStudioBridgeTarget? Target { get; set; }

    public SymbolKey? SymbolKey { get; set; }

    public SourceSpan? Position { get; set; }

    public int MaxResults { get; set; } = 1000;

    public bool IncludeGeneratedCode { get; set; }
}

public sealed class SymbolDescriptionRequest : IVisualStudioBridgeTargetedRequest
{
    public VisualStudioBridgeTarget? Target { get; set; }

    public SymbolKey? SymbolKey { get; set; }

    public SourceSpan? Position { get; set; }

    public bool IncludeGeneratedCode { get; set; }
}

public sealed class DocumentSymbolsRequest : IVisualStudioBridgeTargetedRequest
{
    public VisualStudioBridgeTarget? Target { get; set; }

    public string FilePath { get; set; } = string.Empty;

    public int MaxResults { get; set; } = 1000;

    public bool IncludeGeneratedCode { get; set; }
}

public sealed class VisualStudioDocumentsRequest : IVisualStudioBridgeTargetedRequest
{
    public VisualStudioBridgeTarget? Target { get; set; }

    public int MaxResults { get; set; } = 100;

    public bool IncludeSelection { get; set; } = true;
}

public sealed class SourceNavigationRequest : IVisualStudioBridgeTargetedRequest
{
    public VisualStudioBridgeTarget? Target { get; set; }

    public string FilePath { get; set; } = string.Empty;

    public int Line { get; set; } = 1;

    public int Column { get; set; } = 1;

    public bool Activate { get; set; } = true;
}

public sealed class CallGraphRequest : IVisualStudioBridgeTargetedRequest
{
    public VisualStudioBridgeTarget? Target { get; set; }

    public SymbolKey? SymbolKey { get; set; }

    public SourceSpan? Position { get; set; }

    public int MaxDepth { get; set; } = 1;

    public int MaxResults { get; set; } = 100;

    public bool IncludeGeneratedCode { get; set; }
}

public sealed class SymbolImpactRequest : IVisualStudioBridgeTargetedRequest
{
    public VisualStudioBridgeTarget? Target { get; set; }

    public SymbolKey? SymbolKey { get; set; }

    public SourceSpan? Position { get; set; }

    public int MaxDepth { get; set; } = 1;

    public int MaxResults { get; set; } = 1000;

    public int MaxProjects { get; set; } = 20;

    public int MaxFiles { get; set; } = 20;

    public int MaxContainingTypes { get; set; } = 20;

    public bool IncludeGeneratedCode { get; set; }
}

public sealed class SourceContextRequest : IVisualStudioBridgeTargetedRequest
{
    public VisualStudioBridgeTarget? Target { get; set; }

    public SymbolKey? SymbolKey { get; set; }

    public SourceSpan? Position { get; set; }

    public int ContextLines { get; set; } = 3;

    public int MaxChars { get; set; } = 12000;

    public int MaxSnippets { get; set; } = 3;

    public bool IncludeGeneratedCode { get; set; }
}

public sealed class SourcePositionRequest
{
    public string? SymbolKey { get; set; }

    public string FilePath { get; set; } = string.Empty;

    public int Line { get; set; }

    public int Column { get; set; }
}

public sealed class DerivedTypesRequest : IVisualStudioBridgeTargetedRequest
{
    public VisualStudioBridgeTarget? Target { get; set; }

    public SymbolKey? SymbolKey { get; set; }

    public SourceSpan? Position { get; set; }

    public bool Transitive { get; set; } = true;

    public int MaxResults { get; set; } = 1000;

    public bool IncludeGeneratedCode { get; set; }
}

public sealed class InheritanceChainRequest : IVisualStudioBridgeTargetedRequest
{
    public VisualStudioBridgeTarget? Target { get; set; }

    public SymbolKey? SymbolKey { get; set; }

    public SourceSpan? Position { get; set; }

    public bool IncludeGeneratedCode { get; set; }
}

public sealed class ProjectGraphRequest : IVisualStudioBridgeTargetedRequest
{
    public VisualStudioBridgeTarget? Target { get; set; }

    public string? ProjectName { get; set; }

    public int MaxProjects { get; set; } = 500;

    public int MaxMetadataReferencesPerProject { get; set; } = 50;

    public bool IncludeMetadataReferences { get; set; } = true;
}

public sealed class GeneratedDocumentsRequest : IVisualStudioBridgeTargetedRequest
{
    public VisualStudioBridgeTarget? Target { get; set; }

    public string? ProjectName { get; set; }

    public int MaxResults { get; set; } = 1000;

    public bool IncludeSourceGeneratedDocuments { get; set; } = true;
}

public sealed class TemporaryMarkersRequest : IVisualStudioBridgeTargetedRequest
{
    public VisualStudioBridgeTarget? Target { get; set; }

    public string? FilePath { get; set; }

    public string? ProjectName { get; set; }

    public int MaxResults { get; set; } = 500;

    public bool IncludeGeneratedCode { get; set; }
}

public sealed class EnclosingContextRequest : IVisualStudioBridgeTargetedRequest
{
    public VisualStudioBridgeTarget? Target { get; set; }

    public SourceSpan Position { get; set; } = new();

    public bool IncludeGeneratedCode { get; set; }
}

public sealed class RelatedTestsRequest : IVisualStudioBridgeTargetedRequest
{
    public VisualStudioBridgeTarget? Target { get; set; }

    public SymbolKey? SymbolKey { get; set; }

    public SourceSpan? Position { get; set; }

    public string? FilePath { get; set; }

    public int MaxResults { get; set; } = 100;

    public bool IncludeGeneratedCode { get; set; }
}

public sealed class SymbolReference
{
    public SymbolDescriptor Symbol { get; set; } = new();

    public SourceSpan Span { get; set; } = new();

    public ReferenceRole Role { get; set; }
}

public sealed class SymbolDescription
{
    public SymbolDescriptor Symbol { get; set; } = new();

    public string DisplayString { get; set; } = string.Empty;

    public string DocumentationCommentId { get; set; } = string.Empty;

    public string Accessibility { get; set; } = string.Empty;

    public string ContainingAssembly { get; set; } = string.Empty;

    public string BaseType { get; set; } = string.Empty;

    public IReadOnlyList<string> Interfaces { get; set; } = Array.Empty<string>();

    public IReadOnlyList<string> Attributes { get; set; } = Array.Empty<string>();

    public IReadOnlyList<string> Parameters { get; set; } = Array.Empty<string>();

    public IReadOnlyList<string> TypeParameters { get; set; } = Array.Empty<string>();

    public bool IsStatic { get; set; }

    public bool IsAbstract { get; set; }

    public bool IsVirtual { get; set; }

    public bool IsOverride { get; set; }

    public bool IsSealed { get; set; }

    public bool IsPartial { get; set; }
}

public sealed class SourceContextSnippet
{
    public string ProjectName { get; set; } = string.Empty;

    public string FilePath { get; set; } = string.Empty;

    public SymbolDescriptor? Symbol { get; set; }

    public string ContextKind { get; set; } = string.Empty;

    public SourceSpan FocusSpan { get; set; } = new();

    public SourceSpan SnippetSpan { get; set; } = new();

    public string Text { get; set; } = string.Empty;

    public bool IsTextTruncated { get; set; }

    public string[] Reasons { get; set; } = Array.Empty<string>();
}

public sealed class VisualStudioDocumentSnapshot
{
    public string FilePath { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string ProjectName { get; set; } = string.Empty;

    public bool IsActive { get; set; }

    public bool IsDirty { get; set; }

    public int SelectionStartLine { get; set; }

    public int SelectionStartColumn { get; set; }

    public int SelectionEndLine { get; set; }

    public int SelectionEndColumn { get; set; }
}

public sealed class SourceNavigationResult
{
    public string FilePath { get; set; } = string.Empty;

    public int Line { get; set; }

    public int Column { get; set; }

    public bool Opened { get; set; }

    public bool Activated { get; set; }

    public string[] SuggestedNextSteps { get; set; } = Array.Empty<string>();
}

public sealed class DocumentSymbolNode
{
    public string Id { get; set; } = string.Empty;

    public string ParentId { get; set; } = string.Empty;

    public int Depth { get; set; }

    public SymbolDescriptor Symbol { get; set; } = new();

    public string Detail { get; set; } = string.Empty;
}

public sealed class CallGraphNode
{
    public SymbolDescriptor Symbol { get; set; } = new();

    public int Depth { get; set; }
}

public sealed class CallGraphEdge
{
    public SymbolDescriptor Source { get; set; } = new();

    public SymbolDescriptor Target { get; set; } = new();

    public SourceSpan Span { get; set; } = new();

    public CallGraphEdgeKind Kind { get; set; }

    public int Depth { get; set; } = 1;
}

public sealed class ReferenceRoleCount
{
    public ReferenceRole Role { get; set; }

    public int Count { get; set; }
}

public sealed class SymbolImpactProjectSummary
{
    public string ProjectName { get; set; } = string.Empty;

    public int Count { get; set; }

    public bool IsTargetProject { get; set; }
}

public sealed class SymbolImpactFileSummary
{
    public string ProjectName { get; set; } = string.Empty;

    public string FilePath { get; set; } = string.Empty;

    public int Count { get; set; }

    public IReadOnlyList<ReferenceRoleCount> RoleCounts { get; set; } = Array.Empty<ReferenceRoleCount>();
}

public sealed class SymbolImpactContainingTypeSummary
{
    public string ProjectName { get; set; } = string.Empty;

    public string ContainingType { get; set; } = string.Empty;

    public int Count { get; set; }

    public IReadOnlyList<ReferenceRoleCount> RoleCounts { get; set; } = Array.Empty<ReferenceRoleCount>();
}

public sealed class SymbolImpactSummary
{
    public SymbolDescriptor Symbol { get; set; } = new();

    public int MaxDepth { get; set; } = 1;

    public int TotalReferences { get; set; }

    public int ProcessedReferenceCount { get; set; }

    public int ReferenceCollectionLimit { get; set; }

    public bool IsReferenceCollectionPartial { get; set; }

    public int DirectReferenceCount { get; set; }

    public int TransitiveReferenceCount { get; set; }

    public int DistinctProjectCount { get; set; }

    public int DistinctFileCount { get; set; }

    public int DistinctContainingTypeCount { get; set; }

    public bool HasCrossProjectImpact { get; set; }

    public IReadOnlyList<ReferenceRoleCount> RoleCounts { get; set; } = Array.Empty<ReferenceRoleCount>();

    public IReadOnlyList<SymbolImpactProjectSummary> Projects { get; set; } = Array.Empty<SymbolImpactProjectSummary>();

    public IReadOnlyList<SymbolImpactFileSummary> Files { get; set; } = Array.Empty<SymbolImpactFileSummary>();

    public IReadOnlyList<SymbolImpactContainingTypeSummary> ContainingTypes { get; set; } = Array.Empty<SymbolImpactContainingTypeSummary>();
}

public sealed class DerivedTypeDescriptor
{
    public SymbolDescriptor Symbol { get; set; } = new();

    public string BaseType { get; set; } = string.Empty;

    public int Depth { get; set; }

    public bool IsDirect { get; set; }
}

public sealed class InheritanceChain
{
    public SymbolDescriptor Symbol { get; set; } = new();

    public IReadOnlyList<SymbolDescriptor> BaseTypes { get; set; } = Array.Empty<SymbolDescriptor>();

    public IReadOnlyList<SymbolDescriptor> DirectInterfaces { get; set; } = Array.Empty<SymbolDescriptor>();

    public IReadOnlyList<SymbolDescriptor> AllInterfaces { get; set; } = Array.Empty<SymbolDescriptor>();
}

public sealed class ProjectGraphNode
{
    public string ProjectId { get; set; } = string.Empty;

    public string ProjectName { get; set; } = string.Empty;

    public string AssemblyName { get; set; } = string.Empty;

    public string Language { get; set; } = string.Empty;

    public string FilePath { get; set; } = string.Empty;

    public string TargetFramework { get; set; } = string.Empty;

    public int DocumentCount { get; set; }

    public int ProjectReferenceCount { get; set; }

    public int MetadataReferenceCount { get; set; }

    public IReadOnlyList<string> MetadataReferences { get; set; } = Array.Empty<string>();
}

public sealed class ProjectGraphEdge
{
    public string SourceProjectId { get; set; } = string.Empty;

    public string SourceProjectName { get; set; } = string.Empty;

    public string TargetProjectId { get; set; } = string.Empty;

    public string TargetProjectName { get; set; } = string.Empty;

    public bool TargetProjectIncluded { get; set; } = true;

    public ProjectGraphEdgeKind Kind { get; set; }
}

public sealed class ProjectGraph
{
    public IReadOnlyList<ProjectGraphNode> Nodes { get; set; } = Array.Empty<ProjectGraphNode>();

    public IReadOnlyList<ProjectGraphEdge> Edges { get; set; } = Array.Empty<ProjectGraphEdge>();
}

public sealed class GeneratedDocumentDescriptor
{
    public string ProjectName { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string FilePath { get; set; } = string.Empty;

    public bool IsSourceGenerated { get; set; }

    public bool IsPathGenerated { get; set; }
}

public sealed class TemporaryMarker
{
    public string ProjectName { get; set; } = string.Empty;

    public string Marker { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;

    public string EnclosingSymbol { get; set; } = string.Empty;

    public SourceSpan Span { get; set; } = new();
}

public sealed class EnclosingContext
{
    public SourceSpan Position { get; set; } = new();

    public string Namespace { get; set; } = string.Empty;

    public string Type { get; set; } = string.Empty;

    public string Member { get; set; } = string.Empty;

    public string LocalFunction { get; set; } = string.Empty;

    public string Lambda { get; set; } = string.Empty;

    public IReadOnlyList<SymbolDescriptor> Ancestors { get; set; } = Array.Empty<SymbolDescriptor>();
}

public sealed class RelatedTestDescriptor
{
    public string ProjectName { get; set; } = string.Empty;

    public string TestFramework { get; set; } = string.Empty;

    public string TestClass { get; set; } = string.Empty;

    public string TestMethod { get; set; } = string.Empty;

    public SymbolDescriptor TestSymbol { get; set; } = new();

    public SourceSpan Span { get; set; } = new();

    public IReadOnlyList<string> MatchReasons { get; set; } = Array.Empty<string>();

    public IReadOnlyList<SourceSpan> EvidenceSpans { get; set; } = Array.Empty<SourceSpan>();
}
