using System;
using System.Collections.Immutable;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using VisualStudio.CSharpNavigator.Protocol;
using VisualStudio.CSharpNavigator.Roslyn;
using VisualStudio.CSharpNavigator.Vsix.Bridge;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Rename;
using Microsoft.CodeAnalysis.Simplification;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.LanguageServices;
using Microsoft.VisualStudio.Shell;

namespace VisualStudio.CSharpNavigator.Vsix.Workspace;

internal sealed partial class VisualStudioWorkspaceQueryService
{
    private const int MaxWorkspaceStatusProjects = 200;

    private readonly AsyncPackage _package;
    private VisualStudioWorkspace? _workspace;
    private IComponentModel? _componentModel;

    public VisualStudioWorkspaceQueryService(AsyncPackage package)
    {
        _package = package;
    }

    public async Task<WorkspaceQueryResult<WorkspaceStatus>> GetWorkspaceStatusResultAsync(
        string instanceId,
        int processId,
        CancellationToken cancellationToken)
    {
        var diagnostics = new List<string>();
        var status = await GetWorkspaceStatusAsync(instanceId, processId, diagnostics, cancellationToken)
            .ConfigureAwait(false);

        return new WorkspaceQueryResult<WorkspaceStatus>
        {
            Items = new[] { status },
            Diagnostics = diagnostics,
            IsPartial = diagnostics.Count > 0,
        };
    }

    public Task<WorkspaceStatus> GetWorkspaceStatusAsync(
        string instanceId,
        int processId,
        CancellationToken cancellationToken)
    {
        return GetWorkspaceStatusAsync(instanceId, processId, new List<string>(), cancellationToken);
    }

    public async Task<WorkspaceQueryResult<CallGraphEdge>> FindCallersAsync(
        CallGraphRequest request,
        CancellationToken cancellationToken)
    {
        var validation = ValidateCallGraphRequest(request);
        if (validation is not null)
        {
            return Failure<CallGraphEdge>(validation);
        }

        var symbolResult = await ResolveRequestedSymbolAsync(ToSymbolReferenceRequest(request), cancellationToken)
            .ConfigureAwait(false);
        if (symbolResult.Failure is not null)
        {
            return symbolResult.Failure.As<CallGraphEdge>();
        }

        var targetDescriptor = CreateDescriptor(symbolResult.Symbol!, symbolResult.Solution!, cancellationToken);
        if (targetDescriptor?.Span is null)
        {
            return Failure<CallGraphEdge>("No source descriptor was found for the requested target symbol.");
        }

        return await ExpandCallersAsync(
                symbolResult.Symbol!,
                targetDescriptor,
                symbolResult.Solution!,
                request,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<WorkspaceQueryResult<CallGraphEdge>> FindCalleesAsync(
        CallGraphRequest request,
        CancellationToken cancellationToken)
    {
        var validation = ValidateCallGraphRequest(request);
        if (validation is not null)
        {
            return Failure<CallGraphEdge>(validation);
        }

        var symbolResult = await ResolveRequestedSymbolAsync(ToSymbolReferenceRequest(request), cancellationToken)
            .ConfigureAwait(false);
        if (symbolResult.Failure is not null)
        {
            return symbolResult.Failure.As<CallGraphEdge>();
        }

        var sourceDescriptor = CreateDescriptor(symbolResult.Symbol!, symbolResult.Solution!, cancellationToken);
        if (sourceDescriptor?.Span is null)
        {
            return Failure<CallGraphEdge>("No source descriptor was found for the requested source symbol.");
        }

        return await ExpandCalleesAsync(
                symbolResult.Symbol!,
                sourceDescriptor,
                symbolResult.Solution!,
                request,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<WorkspaceQueryResult<CallGraphEdge>> ExpandCallersAsync(
        ISymbol rootSymbol,
        SymbolDescriptor rootDescriptor,
        Solution solution,
        CallGraphRequest request,
        CancellationToken cancellationToken)
    {
        var items = new List<CallGraphEdge>();
        var diagnostics = new List<string>();
        var expandedSymbols = new HashSet<string>(StringComparer.Ordinal);
        var queuedSymbols = new HashSet<string>(StringComparer.Ordinal)
        {
            CreateCallGraphSymbolIdentity(rootSymbol),
        };
        var emittedEdges = new HashSet<string>(StringComparer.Ordinal);
        var frontier = new List<CallGraphTraversalItem>
        {
            new(rootSymbol, rootDescriptor),
        };

        for (var depth = 1; depth <= request.MaxDepth && frontier.Count > 0; depth++)
        {
            var nextFrontier = new List<CallGraphTraversalItem>();
            foreach (var item in frontier)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!expandedSymbols.Add(CreateCallGraphSymbolIdentity(item.Symbol)))
                {
                    continue;
                }

                var stopped = await VisitDirectCallerEdgesAsync(
                        item.Symbol,
                        item.Descriptor,
                        solution,
                        request.IncludeGeneratedCode,
                        depth,
                        candidate => VisitCallGraphCandidate(
                            candidate,
                            items,
                            nextFrontier,
                            queuedSymbols,
                            emittedEdges,
                            depth,
                            request),
                        cancellationToken)
                    .ConfigureAwait(false);
                if (stopped)
                {
                    diagnostics.Add($"Callers were truncated at MaxResults={request.MaxResults} before completing MaxDepth={request.MaxDepth}.");
                    return Success(items.ToArray(), diagnostics, isPartial: true);
                }
            }

            frontier = nextFrontier;
        }

        return Success(items.ToArray(), diagnostics, isPartial: diagnostics.Count > 0);
    }

    private static async Task<WorkspaceQueryResult<CallGraphEdge>> ExpandCalleesAsync(
        ISymbol rootSymbol,
        SymbolDescriptor rootDescriptor,
        Solution solution,
        CallGraphRequest request,
        CancellationToken cancellationToken)
    {
        var items = new List<CallGraphEdge>();
        var diagnostics = new List<string>();
        var expandedSymbols = new HashSet<string>(StringComparer.Ordinal);
        var queuedSymbols = new HashSet<string>(StringComparer.Ordinal)
        {
            CreateCallGraphSymbolIdentity(rootSymbol),
        };
        var emittedEdges = new HashSet<string>(StringComparer.Ordinal);
        var frontier = new List<CallGraphTraversalItem>
        {
            new(rootSymbol, rootDescriptor),
        };

        for (var depth = 1; depth <= request.MaxDepth && frontier.Count > 0; depth++)
        {
            var nextFrontier = new List<CallGraphTraversalItem>();
            foreach (var item in frontier)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!expandedSymbols.Add(CreateCallGraphSymbolIdentity(item.Symbol)))
                {
                    continue;
                }

                var stopped = await VisitDirectCalleeEdgesAsync(
                        item.Symbol,
                        item.Descriptor,
                        solution,
                        request.IncludeGeneratedCode,
                        depth,
                        diagnostics,
                        candidate => VisitCallGraphCandidate(
                            candidate,
                            items,
                            nextFrontier,
                            queuedSymbols,
                            emittedEdges,
                            depth,
                            request),
                        cancellationToken)
                    .ConfigureAwait(false);
                if (stopped)
                {
                    diagnostics.Add($"Callees were truncated at MaxResults={request.MaxResults} before completing MaxDepth={request.MaxDepth}.");
                    return Success(items.ToArray(), diagnostics, isPartial: true);
                }
            }

            frontier = nextFrontier;
        }

        return Success(items.ToArray(), diagnostics, isPartial: diagnostics.Count > 0);
    }

    private static bool VisitCallGraphCandidate(
        CallGraphTraversalEdge candidate,
        List<CallGraphEdge> items,
        List<CallGraphTraversalItem> nextFrontier,
        HashSet<string> queuedSymbols,
        HashSet<string> emittedEdges,
        int depth,
        CallGraphRequest request)
    {
        if (!emittedEdges.Add(CreateCallGraphEdgeIdentity(candidate.Edge)))
        {
            return true;
        }

        items.Add(candidate.Edge);
        if (depth < request.MaxDepth)
        {
            var nextSymbolIdentity = CreateCallGraphSymbolIdentity(candidate.NextSymbol);
            if (queuedSymbols.Add(nextSymbolIdentity))
            {
                nextFrontier.Add(new CallGraphTraversalItem(candidate.NextSymbol, candidate.NextDescriptor));
            }
        }

        return items.Count < request.MaxResults;
    }

    public async Task<WorkspaceQueryResult<SymbolImpactSummary>> AnalyzeSymbolImpactAsync(
        SymbolImpactRequest request,
        CancellationToken cancellationToken)
    {
        var validation = ValidateSymbolImpactRequest(request);
        if (validation is not null)
        {
            return Failure<SymbolImpactSummary>(validation);
        }

        var symbolResult = await ResolveRequestedSymbolAsync(ToSymbolReferenceRequest(request), cancellationToken)
            .ConfigureAwait(false);
        if (symbolResult.Failure is not null)
        {
            return symbolResult.Failure.As<SymbolImpactSummary>();
        }

        var targetDescriptor = CreateDescriptor(symbolResult.Symbol!, symbolResult.Solution!, cancellationToken);
        if (targetDescriptor?.Span is null)
        {
            return Failure<SymbolImpactSummary>("No source descriptor was found for the requested target symbol.");
        }

        var siteCollection = await CollectSymbolImpactSitesAsync(
                symbolResult.Symbol!,
                symbolResult.Solution!,
                request,
                cancellationToken)
            .ConfigureAwait(false);
        if (siteCollection.Failure is not null)
        {
            return siteCollection.Failure.As<SymbolImpactSummary>();
        }

        var aggregation = SymbolImpactAggregator.Aggregate(
            targetDescriptor,
            siteCollection.Sites!,
            new SymbolImpactAggregationOptions
            {
                MaxReferences = request.MaxResults,
                IsReferenceCollectionPartial = siteCollection.IsPartial,
                MaxDepth = request.MaxDepth,
                MaxProjects = request.MaxProjects,
                MaxFiles = request.MaxFiles,
                MaxContainingTypes = request.MaxContainingTypes,
            });

        var diagnostics = siteCollection.Diagnostics!.Concat(aggregation.Diagnostics).ToArray();
        var isPartial = siteCollection.IsPartial || aggregation.IsPartial;
        return Success(new[] { aggregation.Summary }, diagnostics, isPartial);
    }

    public async Task<WorkspaceQueryResult<DerivedTypeDescriptor>> FindDerivedTypesAsync(
        DerivedTypesRequest request,
        CancellationToken cancellationToken)
    {
        if (request.MaxResults is < 1 or > 5000)
        {
            return Failure<DerivedTypeDescriptor>("MaxResults must be between 1 and 5000.");
        }

        var symbolResult = await ResolveRequestedSymbolAsync(ToSymbolReferenceRequest(request), cancellationToken)
            .ConfigureAwait(false);
        if (symbolResult.Failure is not null)
        {
            return symbolResult.Failure.As<DerivedTypeDescriptor>();
        }

        if (symbolResult.Symbol is not INamedTypeSymbol targetType)
        {
            return Failure<DerivedTypeDescriptor>("The requested symbol is not a named type.");
        }

        var solution = symbolResult.Solution!;
        var diagnostics = new List<string>();
        var items = new List<DerivedTypeDescriptor>();
        var seen = new HashSet<ISymbol>(SymbolEqualityComparer.Default);

        foreach (var symbol in await SymbolFinder.FindDerivedClassesAsync(
                     targetType,
                     solution,
                     projects: null,
                     transitive: request.Transitive,
                     cancellationToken: cancellationToken).ConfigureAwait(false))
        {
            AddDerivedTypeDescriptor(symbol, targetType, solution, request, items, seen, cancellationToken);
            if (items.Count >= request.MaxResults)
            {
                diagnostics.Add($"Derived types were truncated at MaxResults={request.MaxResults}.");
                return Success(items.ToArray(), diagnostics, isPartial: true);
            }
        }

        foreach (var symbol in await SymbolFinder.FindDerivedInterfacesAsync(
                     targetType,
                     solution,
                     projects: null,
                     transitive: request.Transitive,
                     cancellationToken: cancellationToken).ConfigureAwait(false))
        {
            AddDerivedTypeDescriptor(symbol, targetType, solution, request, items, seen, cancellationToken);
            if (items.Count >= request.MaxResults)
            {
                diagnostics.Add($"Derived types were truncated at MaxResults={request.MaxResults}.");
                return Success(items.ToArray(), diagnostics, isPartial: true);
            }
        }

        if (targetType.TypeKind == TypeKind.Interface)
        {
            foreach (var symbol in await SymbolFinder.FindImplementationsAsync(
                         targetType,
                         solution,
                         projects: null,
                         cancellationToken: cancellationToken).ConfigureAwait(false))
            {
                if (symbol is not INamedTypeSymbol namedType)
                {
                    continue;
                }

                if (!request.Transitive && !DirectlyImplementsInterface(namedType, targetType))
                {
                    continue;
                }

                AddDerivedTypeDescriptor(namedType, targetType, solution, request, items, seen, cancellationToken);
                if (items.Count >= request.MaxResults)
                {
                    diagnostics.Add($"Derived types were truncated at MaxResults={request.MaxResults}.");
                    return Success(items.ToArray(), diagnostics, isPartial: true);
                }
            }
        }

        return Success(
            items.OrderBy(item => item.Depth <= 0 ? int.MaxValue : item.Depth)
                .ThenBy(item => item.Symbol.ProjectName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.Symbol.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            diagnostics,
            isPartial: false);
    }

    public async Task<WorkspaceQueryResult<InheritanceChain>> GetInheritanceChainAsync(
        InheritanceChainRequest request,
        CancellationToken cancellationToken)
    {
        var symbolResult = await ResolveRequestedSymbolAsync(ToSymbolReferenceRequest(request), cancellationToken)
            .ConfigureAwait(false);
        if (symbolResult.Failure is not null)
        {
            return symbolResult.Failure.As<InheritanceChain>();
        }

        if (symbolResult.Symbol is not INamedTypeSymbol namedType)
        {
            return Failure<InheritanceChain>("The requested symbol is not a named type.");
        }

        var descriptor = CreateDescriptorIncludingMetadata(namedType, symbolResult.Solution!, cancellationToken);
        if (descriptor.Span is not null && !request.IncludeGeneratedCode && IsGeneratedPath(descriptor.Span.FilePath))
        {
            return Failure<InheritanceChain>("The requested type is in generated code. Retry with IncludeGeneratedCode=true.");
        }

        var baseTypes = new List<SymbolDescriptor>();
        for (var current = namedType.BaseType; current is not null; current = current.BaseType)
        {
            cancellationToken.ThrowIfCancellationRequested();
            baseTypes.Add(CreateDescriptorIncludingMetadata(current, symbolResult.Solution!, cancellationToken));
        }

        var chain = new InheritanceChain
        {
            Symbol = descriptor,
            BaseTypes = baseTypes.ToArray(),
            DirectInterfaces = namedType.Interfaces
                .Select(symbol => CreateDescriptorIncludingMetadata(symbol, symbolResult.Solution!, cancellationToken))
                .ToArray(),
            AllInterfaces = namedType.AllInterfaces
                .Select(symbol => CreateDescriptorIncludingMetadata(symbol, symbolResult.Solution!, cancellationToken))
                .ToArray(),
        };

        return Success(new[] { chain });
    }

    public async Task<WorkspaceQueryResult<ProjectGraph>> GetProjectGraphAsync(
        ProjectGraphRequest request,
        CancellationToken cancellationToken)
    {
        if (request.MaxProjects is < 1 or > 1000)
        {
            return Failure<ProjectGraph>("MaxProjects must be between 1 and 1000.");
        }

        if (request.MaxMetadataReferencesPerProject is < 0 or > 500)
        {
            return Failure<ProjectGraph>("MaxMetadataReferencesPerProject must be between 0 and 500.");
        }

        var solutionResult = await GetRequiredSolutionAsync(cancellationToken).ConfigureAwait(false);
        if (solutionResult.Failure is not null)
        {
            return solutionResult.Failure.As<ProjectGraph>();
        }

        var allProjects = solutionResult.Solution!.Projects
            .Where(project => project.Language == LanguageNames.CSharp)
            .ToArray();
        var projects = allProjects
            .Where(project => string.IsNullOrWhiteSpace(request.ProjectName)
                || string.Equals(project.Name, request.ProjectName, StringComparison.OrdinalIgnoreCase))
            .Take(request.MaxProjects)
            .ToArray();

        if (!string.IsNullOrWhiteSpace(request.ProjectName) && projects.Length == 0)
        {
            return Failure<ProjectGraph>("ProjectNotFound: no C# project matched the requested project name.");
        }

        var diagnostics = new List<string>();
        var isPartial = false;
        if (projects.Length < allProjects.Length && string.IsNullOrWhiteSpace(request.ProjectName))
        {
            diagnostics.Add($"Project graph was truncated at MaxProjects={request.MaxProjects}.");
            isPartial = true;
        }

        var nodes = projects
            .Select(project => CreateProjectGraphNode(project, request, diagnostics))
            .ToArray();
        if (diagnostics.Count > 0)
        {
            isPartial = true;
        }

        var returnedProjectIds = new HashSet<string>(
            nodes.Select(node => node.ProjectId),
            StringComparer.OrdinalIgnoreCase);
        var edges = projects
            .SelectMany(project => project.ProjectReferences.Select(reference => CreateProjectGraphEdge(project, reference, solutionResult.Solution!, returnedProjectIds)))
            .Where(edge => edge is not null)
            .Cast<ProjectGraphEdge>()
            .ToArray();
        if (edges.Any(edge => !edge.TargetProjectIncluded))
        {
            diagnostics.Add("Project graph contains outgoing references to projects that are outside the returned node set; those edges are marked with TargetProjectIncluded=false.");
            isPartial = true;
        }

        return Success(
            new[]
            {
                new ProjectGraph
                {
                    Nodes = nodes,
                    Edges = edges,
                },
            },
            diagnostics,
            isPartial);
    }

    public async Task<WorkspaceQueryResult<GeneratedDocumentDescriptor>> ListGeneratedDocumentsAsync(
        GeneratedDocumentsRequest request,
        CancellationToken cancellationToken)
    {
        if (request.MaxResults is < 1 or > 10000)
        {
            return Failure<GeneratedDocumentDescriptor>("MaxResults must be between 1 and 10000.");
        }

        var solutionResult = await GetRequiredSolutionAsync(cancellationToken).ConfigureAwait(false);
        if (solutionResult.Failure is not null)
        {
            return solutionResult.Failure.As<GeneratedDocumentDescriptor>();
        }

        var projects = solutionResult.Solution!.Projects
            .Where(project => project.Language == LanguageNames.CSharp)
            .Where(project => string.IsNullOrWhiteSpace(request.ProjectName)
                || string.Equals(project.Name, request.ProjectName, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (!string.IsNullOrWhiteSpace(request.ProjectName) && projects.Length == 0)
        {
            return Failure<GeneratedDocumentDescriptor>("ProjectNotFound: no C# project matched the requested project name.");
        }

        var items = new List<GeneratedDocumentDescriptor>();
        var diagnostics = new List<string>();
        foreach (var project in projects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var document in project.Documents.Where(IsGeneratedDocument))
            {
                items.Add(new GeneratedDocumentDescriptor
                {
                    ProjectName = project.Name,
                    Name = document.Name,
                    FilePath = document.FilePath ?? string.Empty,
                    IsPathGenerated = true,
                    IsSourceGenerated = false,
                });

                if (items.Count >= request.MaxResults)
                {
                    diagnostics.Add($"Generated documents were truncated at MaxResults={request.MaxResults}.");
                    return Success(items.ToArray(), diagnostics, isPartial: true);
                }
            }

            if (!request.IncludeSourceGeneratedDocuments)
            {
                continue;
            }

            try
            {
                var generatedDocuments = await project.GetSourceGeneratedDocumentsAsync(cancellationToken)
                    .ConfigureAwait(false);
                foreach (var document in generatedDocuments)
                {
                    items.Add(new GeneratedDocumentDescriptor
                    {
                        ProjectName = project.Name,
                        Name = document.Name,
                        FilePath = document.FilePath ?? string.Empty,
                        IsPathGenerated = IsGeneratedDocument(document),
                        IsSourceGenerated = true,
                    });

                    if (items.Count >= request.MaxResults)
                    {
                        diagnostics.Add($"Generated documents were truncated at MaxResults={request.MaxResults}.");
                        return Success(items.ToArray(), diagnostics, isPartial: true);
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                diagnostics.Add($"Source-generated documents could not be read for project '{project.Name}': {ex.Message}");
            }
        }

        return Success(items.ToArray(), diagnostics, isPartial: diagnostics.Count > 0);
    }

    public async Task<WorkspaceQueryResult<TemporaryMarker>> FindTemporaryMarkersAsync(
        TemporaryMarkersRequest request,
        CancellationToken cancellationToken)
    {
        if (request.MaxResults is < 1 or > 10000)
        {
            return Failure<TemporaryMarker>("MaxResults must be between 1 and 10000.");
        }

        var solutionResult = await GetRequiredSolutionAsync(cancellationToken).ConfigureAwait(false);
        if (solutionResult.Failure is not null)
        {
            return solutionResult.Failure.As<TemporaryMarker>();
        }

        var requestedDocument = string.IsNullOrWhiteSpace(request.FilePath)
            ? null
            : await FindDocumentByPathAsync(
                    solutionResult.Solution!,
                    request.FilePath!,
                    request.IncludeGeneratedCode,
                    cancellationToken)
                .ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(request.FilePath) && requestedDocument is null)
        {
            return Failure<TemporaryMarker>("DocumentNotFound: the requested file is not in the active solution.");
        }

        var diagnostics = new List<string>
        {
            "Temporary marker scanning is an audit aid based on syntax trivia text; it is not a proof of implementation completeness.",
        };
        var documents = new List<Document>();
        foreach (var project in solutionResult.Solution!.Projects
                     .Where(project => project.Language == LanguageNames.CSharp)
                     .Where(project => string.IsNullOrWhiteSpace(request.ProjectName)
                         || string.Equals(project.Name, request.ProjectName, StringComparison.OrdinalIgnoreCase)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            documents.AddRange((await EnumerateProjectDocumentsAsync(project, request.IncludeGeneratedCode, diagnostics, cancellationToken)
                    .ConfigureAwait(false))
                .Where(document => requestedDocument is null || document.Id == requestedDocument.Id)
                .Where(document => request.IncludeGeneratedCode || !IsGeneratedDocument(document)));
        }

        if (!string.IsNullOrWhiteSpace(request.ProjectName) && documents.Count == 0)
        {
            return Failure<TemporaryMarker>("ProjectNotFound: no C# project or document matched the requested scope.");
        }

        var items = new List<TemporaryMarker>();
        foreach (var document in documents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var syntaxRoot = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
            var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
            if (syntaxRoot is null)
            {
                continue;
            }

            foreach (var trivia in syntaxRoot.DescendantTrivia(descendIntoTrivia: true))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var text = trivia.ToFullString();
                var marker = FindTemporaryMarker(text);
                if (marker is null)
                {
                    continue;
                }

                var span = CreateSpan(trivia.GetLocation());
                if (span is null)
                {
                    continue;
                }

                var enclosingSymbol = semanticModel is null
                    ? string.Empty
                    : FindContainingDeclaredSymbol(trivia.Token.Parent, semanticModel, cancellationToken)?
                        .ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat) ?? string.Empty;

                items.Add(new TemporaryMarker
                {
                    ProjectName = document.Project.Name,
                    Marker = marker,
                    Text = Truncate(text.Trim(), 500),
                    EnclosingSymbol = enclosingSymbol,
                    Span = span,
                });

                if (items.Count >= request.MaxResults)
                {
                    diagnostics.Add($"Temporary markers were truncated at MaxResults={request.MaxResults}.");
                    return Success(items.ToArray(), diagnostics, isPartial: true);
                }
            }

            foreach (var node in syntaxRoot.DescendantNodes())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var marker = FindTemporaryMarker(node);
                if (marker is null)
                {
                    continue;
                }

                var span = CreateSpan(node.GetLocation());
                if (span is null)
                {
                    continue;
                }

                var enclosingSymbol = semanticModel is null
                    ? string.Empty
                    : FindContainingDeclaredSymbol(node, semanticModel, cancellationToken)?
                        .ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat) ?? string.Empty;

                items.Add(new TemporaryMarker
                {
                    ProjectName = document.Project.Name,
                    Marker = marker,
                    Text = Truncate(node.ToString().Trim(), 500),
                    EnclosingSymbol = enclosingSymbol,
                    Span = span,
                });

                if (items.Count >= request.MaxResults)
                {
                    diagnostics.Add($"Temporary markers were truncated at MaxResults={request.MaxResults}.");
                    return Success(items.ToArray(), diagnostics, isPartial: true);
                }
            }
        }

        return Success(items.ToArray(), diagnostics, isPartial: diagnostics.Count > 1);
    }

    public async Task<WorkspaceQueryResult<EnclosingContext>> GetEnclosingContextAsync(
        EnclosingContextRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Position.FilePath))
        {
            return Failure<EnclosingContext>("FilePath is required.");
        }

        if (request.Position.StartLine <= 0 || request.Position.StartColumn <= 0)
        {
            return Failure<EnclosingContext>("Line and column must be one-based positive integers.");
        }

        var solutionResult = await GetRequiredSolutionAsync(cancellationToken).ConfigureAwait(false);
        if (solutionResult.Failure is not null)
        {
            return solutionResult.Failure.As<EnclosingContext>();
        }

        var document = await FindDocumentByPathAsync(
                solutionResult.Solution!,
                request.Position.FilePath,
                request.IncludeGeneratedCode,
                cancellationToken)
            .ConfigureAwait(false);
        if (document is null)
        {
            return Failure<EnclosingContext>("DocumentNotFound: the requested file is not in the active solution.");
        }

        if (!request.IncludeGeneratedCode && IsGeneratedDocument(document))
        {
            return Failure<EnclosingContext>("The requested document is generated code. Retry with IncludeGeneratedCode=true.");
        }

        var sourceText = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
        var absolutePosition = GetAbsolutePosition(sourceText, request.Position.StartLine, request.Position.StartColumn);
        if (absolutePosition is null)
        {
            return Failure<EnclosingContext>("The supplied line and column are outside the document.");
        }

        var syntaxRoot = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (syntaxRoot is null || semanticModel is null)
        {
            return Failure<EnclosingContext>("RoslynQueryFailed: document has no syntax root or semantic model.");
        }

        var token = syntaxRoot.FindToken(absolutePosition.Value);
        var node = token.Parent;
        var context = new EnclosingContext
        {
            Position = request.Position,
            Namespace = FindNearestSymbolDisplay<NamespaceDeclarationSyntax>(node, semanticModel, cancellationToken)
                ?? FindNearestSymbolDisplay<FileScopedNamespaceDeclarationSyntax>(node, semanticModel, cancellationToken)
                ?? string.Empty,
            Type = FindNearestSymbolDisplay<BaseTypeDeclarationSyntax>(node, semanticModel, cancellationToken) ?? string.Empty,
            Member = FindNearestMemberDisplay(node, semanticModel, cancellationToken),
            LocalFunction = FindNearestSymbolDisplay<LocalFunctionStatementSyntax>(node, semanticModel, cancellationToken) ?? string.Empty,
            Lambda = FindNearestLambdaDisplay(node),
            Ancestors = CreateEnclosingAncestorDescriptors(node, semanticModel, document.Project, cancellationToken),
        };

        return Success(new[] { context });
    }

    public async Task<WorkspaceQueryResult<RelatedTestDescriptor>> FindRelatedTestsAsync(
        RelatedTestsRequest request,
        CancellationToken cancellationToken)
    {
        if (request.MaxResults is < 1 or > 1000)
        {
            return Failure<RelatedTestDescriptor>("MaxResults must be between 1 and 1000.");
        }

        if (request.SymbolKey is null
            && request.Position is null
            && string.IsNullOrWhiteSpace(request.FilePath))
        {
            return Failure<RelatedTestDescriptor>("Provide either a symbol key, source position, or source file path.");
        }

        if (request.Position is not null
            && (string.IsNullOrWhiteSpace(request.Position.FilePath)
                || request.Position.StartLine <= 0
                || request.Position.StartColumn <= 0))
        {
            return Failure<RelatedTestDescriptor>("Source position requires filePath, one-based line, and one-based column.");
        }

        var solutionResult = await GetRequiredSolutionAsync(cancellationToken).ConfigureAwait(false);
        if (solutionResult.Failure is not null)
        {
            return solutionResult.Failure.As<RelatedTestDescriptor>();
        }

        var solution = solutionResult.Solution!;
        var targetResult = await ResolveRelatedTestsTargetAsync(solution, request, cancellationToken)
            .ConfigureAwait(false);
        if (targetResult.Failure is not null)
        {
            return targetResult.Failure.As<RelatedTestDescriptor>();
        }

        var testProjects = solution.Projects
            .Where(project => project.Language == LanguageNames.CSharp)
            .Where(IsLikelyTestProject)
            .ToDictionary(project => project.Id, DetectTestFramework);

        var diagnostics = new List<string>
        {
            "Related test discovery combines Roslyn references with naming heuristics; ReferenceMatch is the only direct symbol-level evidence.",
        };

        if (testProjects.Count == 0)
        {
            diagnostics.Add("No C# test projects were detected in the active solution by project name/path or common test framework references.");
            return Success(Array.Empty<RelatedTestDescriptor>(), diagnostics);
        }

        var accumulator = new RelatedTestAccumulator(request.MaxResults);
        if (targetResult.Target!.Symbol is not null)
        {
            await AddReferenceRelatedTestsAsync(
                    targetResult.Target.Symbol,
                    solution,
                    testProjects,
                    request.IncludeGeneratedCode,
                    accumulator,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            diagnostics.Add("No target symbol was resolved; results are based on source-file naming heuristics only.");
        }

        if (!accumulator.IsTruncated)
        {
            await AddHeuristicRelatedTestsAsync(
                    solution,
                    testProjects,
                    targetResult.Target,
                    request.IncludeGeneratedCode,
                    accumulator,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        if (accumulator.IsTruncated)
        {
            diagnostics.Add($"Related test discovery was truncated at MaxResults={request.MaxResults}.");
        }

        if (targetResult.Target.Symbol is not null && !accumulator.HasReferenceMatch)
        {
            diagnostics.Add("No direct Roslyn reference matches were found in detected test projects; returned results, if any, are heuristic.");
        }

        var items = accumulator.ToDescriptors();
        if (items.Count == 0)
        {
            diagnostics.Add("No related tests were found for the supplied target in detected test projects.");
        }

        return Success(items, diagnostics, accumulator.IsTruncated);
    }


    private static SymbolReferenceRequest ToSymbolReferenceRequest(SymbolDescriptionRequest request)
    {
        return new SymbolReferenceRequest
        {
            SymbolKey = request.SymbolKey,
            Position = request.Position,
            IncludeGeneratedCode = request.IncludeGeneratedCode,
        };
    }

    private static SymbolReferenceRequest ToSymbolReferenceRequest(SourceContextRequest request)
    {
        return new SymbolReferenceRequest
        {
            SymbolKey = request.SymbolKey,
            Position = request.Position,
            IncludeGeneratedCode = request.IncludeGeneratedCode,
        };
    }

    private static SymbolReferenceRequest ToSymbolReferenceRequest(CallGraphRequest request)
    {
        return new SymbolReferenceRequest
        {
            SymbolKey = request.SymbolKey,
            Position = request.Position,
            IncludeGeneratedCode = request.IncludeGeneratedCode,
        };
    }

    private static SymbolReferenceRequest ToSymbolReferenceRequest(SymbolImpactRequest request)
    {
        return new SymbolReferenceRequest
        {
            SymbolKey = request.SymbolKey,
            Position = request.Position,
            IncludeGeneratedCode = request.IncludeGeneratedCode,
        };
    }

    private static SymbolReferenceRequest ToSymbolReferenceRequest(DerivedTypesRequest request)
    {
        return new SymbolReferenceRequest
        {
            SymbolKey = request.SymbolKey,
            Position = request.Position,
            IncludeGeneratedCode = request.IncludeGeneratedCode,
        };
    }

    private static SymbolReferenceRequest ToSymbolReferenceRequest(InheritanceChainRequest request)
    {
        return new SymbolReferenceRequest
        {
            SymbolKey = request.SymbolKey,
            Position = request.Position,
            IncludeGeneratedCode = request.IncludeGeneratedCode,
        };
    }

    private static SymbolReferenceRequest ToSymbolReferenceRequest(RenamePreviewRequest request)
    {
        return new SymbolReferenceRequest
        {
            SymbolKey = request.SymbolKey,
            Position = request.Position,
            IncludeGeneratedCode = request.IncludeGeneratedCode,
        };
    }

    private static SymbolReferenceRequest ToSymbolReferenceRequest(RenameApplyRequest request)
    {
        return new SymbolReferenceRequest
        {
            SymbolKey = request.SymbolKey,
            Position = request.Position,
            IncludeGeneratedCode = request.IncludeGeneratedCode,
        };
    }

    private static RenamePreviewRequest ToRenamePreviewRequest(RenameApplyRequest request)
    {
        return new RenamePreviewRequest
        {
            Target = request.Target,
            SymbolKey = request.SymbolKey,
            Position = request.Position,
            NewName = request.NewName,
            RenameOverloads = request.RenameOverloads,
            RenameInStrings = request.RenameInStrings,
            RenameInComments = request.RenameInComments,
            RenameFile = request.RenameFile,
            MaxTextChanges = request.MaxTextChanges,
            MaxSnippetLength = request.MaxSnippetLength,
            IncludeGeneratedCode = request.IncludeGeneratedCode,
        };
    }


    private static SourceSpan CreateSpan(string filePath, SourceText sourceText, TextSpan textSpan)
    {
        var lineSpan = sourceText.Lines.GetLinePositionSpan(textSpan);
        return new SourceSpan
        {
            FilePath = filePath,
            StartLine = lineSpan.Start.Line + 1,
            StartColumn = lineSpan.Start.Character + 1,
            EndLine = lineSpan.End.Line + 1,
            EndColumn = lineSpan.End.Character + 1,
        };
    }

    private static string TruncateSnippet(string value, int maxLength, out bool isTruncated)
    {
        if (maxLength == 0)
        {
            isTruncated = value.Length > 0;
            return string.Empty;
        }

        if (value.Length <= maxLength)
        {
            isTruncated = false;
            return value;
        }

        isTruncated = true;
        return value.Substring(0, maxLength);
    }

    private static SourceContextSnippet CreateSourceContextSnippet(
        Document document,
        SourceText sourceText,
        TextSpan focusSpan,
        TextSpan sourceSpan,
        SymbolDescriptor? symbol,
        string contextKind,
        int contextLines,
        int maxChars,
        IReadOnlyList<string> reasons)
    {
        var snippetTextSpan = CreateSnippetTextSpan(sourceText, sourceSpan, focusSpan, contextLines, maxChars, out var isTruncated);
        return new SourceContextSnippet
        {
            ProjectName = document.Project.Name,
            FilePath = document.FilePath ?? document.Name,
            Symbol = symbol,
            ContextKind = contextKind,
            FocusSpan = CreateSpan(document.FilePath ?? document.Name, sourceText, focusSpan),
            SnippetSpan = CreateSpan(document.FilePath ?? document.Name, sourceText, snippetTextSpan),
            Text = sourceText.ToString(snippetTextSpan),
            IsTextTruncated = isTruncated,
            Reasons = reasons.ToArray(),
        };
    }

    private static TextSpan CreateSnippetTextSpan(
        SourceText sourceText,
        TextSpan sourceSpan,
        TextSpan focusSpan,
        int contextLines,
        int maxChars,
        out bool isTruncated)
    {
        var lineSpan = sourceText.Lines.GetLinePositionSpan(sourceSpan);
        var startLine = Math.Max(0, lineSpan.Start.Line - contextLines);
        var endLine = Math.Min(sourceText.Lines.Count - 1, lineSpan.End.Line + contextLines);
        var start = sourceText.Lines[startLine].Start;
        var end = sourceText.Lines[endLine].EndIncludingLineBreak;
        if (end < start)
        {
            end = start;
        }

        var length = end - start;
        if (length <= maxChars)
        {
            isTruncated = false;
            return TextSpan.FromBounds(start, end);
        }

        var truncatedStart = Math.Max(start, Math.Min(focusSpan.Start, end - maxChars));
        isTruncated = true;
        return new TextSpan(truncatedStart, maxChars);
    }

    private static SyntaxNode? FindSourceContextNode(SyntaxNode? node)
    {
        for (var current = node; current is not null; current = current.Parent)
        {
            switch (current)
            {
                case MethodDeclarationSyntax:
                case ConstructorDeclarationSyntax:
                case DestructorDeclarationSyntax:
                case OperatorDeclarationSyntax:
                case ConversionOperatorDeclarationSyntax:
                case PropertyDeclarationSyntax:
                case EventDeclarationSyntax:
                case FieldDeclarationSyntax:
                case LocalFunctionStatementSyntax:
                case AnonymousFunctionExpressionSyntax:
                    return current;
            }
        }

        for (var current = node; current is not null; current = current.Parent)
        {
            if (current is BaseTypeDeclarationSyntax)
            {
                return current;
            }
        }

        return node;
    }

    private static string DescribeSourceContextKind(SyntaxNode? node)
    {
        return node switch
        {
            MethodDeclarationSyntax => "method",
            ConstructorDeclarationSyntax => "constructor",
            DestructorDeclarationSyntax => "destructor",
            OperatorDeclarationSyntax => "operator",
            ConversionOperatorDeclarationSyntax => "conversion operator",
            PropertyDeclarationSyntax => "property",
            EventDeclarationSyntax => "event",
            FieldDeclarationSyntax => "field",
            LocalFunctionStatementSyntax => "local function",
            AnonymousFunctionExpressionSyntax => "lambda",
            BaseTypeDeclarationSyntax => "type",
            _ => "source context",
        };
    }

    private static bool IsPartialSymbol(ISymbol symbol)
    {
        return symbol.DeclaringSyntaxReferences.Length > 1;
    }

    private static async Task<RelatedTestsTargetResult> ResolveRelatedTestsTargetAsync(
        Solution solution,
        RelatedTestsRequest request,
        CancellationToken cancellationToken)
    {
        ISymbol? symbol = null;
        Document? document = null;
        Project? project = null;

        if (request.SymbolKey is not null)
        {
            symbol = await ResolveSymbolKeyAsync(solution, request.SymbolKey.Value, cancellationToken)
                .ConfigureAwait(false);
            if (symbol is null)
            {
                return RelatedTestsTargetResult.FromFailure(
                    "RoslynQueryFailed: the supplied SymbolKey could not be resolved in the current solution.");
            }
        }
        else if (request.Position is not null)
        {
            symbol = await ResolveSymbolAtPositionAsync(solution, request.Position, request.IncludeGeneratedCode, cancellationToken)
                .ConfigureAwait(false);
            if (symbol is null)
            {
                return RelatedTestsTargetResult.FromFailure(
                    "RoslynQueryFailed: no symbol was found at the supplied source position.");
            }
        }

        if (symbol is not null)
        {
            project = FindProjectForSymbol(solution, symbol, cancellationToken);
            var sourceTree = symbol.Locations.FirstOrDefault(location => location.IsInSource)?.SourceTree;
            if (sourceTree is not null)
            {
                document = solution.GetDocument(sourceTree);
            }
        }

        if (request.Position is not null)
        {
            document = await FindDocumentByPathAsync(solution, request.Position.FilePath, request.IncludeGeneratedCode, cancellationToken)
                .ConfigureAwait(false);
            if (document is null)
            {
                return RelatedTestsTargetResult.FromFailure("DocumentNotFound: the requested source position file is not in the active solution.");
            }
        }
        else if (!string.IsNullOrWhiteSpace(request.FilePath))
        {
            document = await FindDocumentByPathAsync(solution, request.FilePath!, request.IncludeGeneratedCode, cancellationToken)
                .ConfigureAwait(false);
            if (document is null)
            {
                return RelatedTestsTargetResult.FromFailure("DocumentNotFound: the requested file is not in the active solution.");
            }
        }

        if (document is not null && !request.IncludeGeneratedCode && IsGeneratedDocument(document))
        {
            return RelatedTestsTargetResult.FromFailure("The requested document is generated code. Retry with IncludeGeneratedCode=true.");
        }

        project ??= document?.Project;
        if (symbol is null && document is null)
        {
            return RelatedTestsTargetResult.FromFailure("MalformedRequest: provide either SymbolKey, source position, or FilePath.");
        }

        var target = await CreateRelatedTestsTargetContextAsync(symbol, document, project, cancellationToken)
            .ConfigureAwait(false);
        return RelatedTestsTargetResult.Success(target);
    }

    private static async Task<RelatedTestsTargetContext> CreateRelatedTestsTargetContextAsync(
        ISymbol? symbol,
        Document? document,
        Project? project,
        CancellationToken cancellationToken)
    {
        var needles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sourceNamespace = string.Empty;

        if (symbol is not null)
        {
            AddSymbolNeedles(symbol, needles);
            sourceNamespace = symbol.ContainingNamespace is null || symbol.ContainingNamespace.IsGlobalNamespace
                ? string.Empty
                : symbol.ContainingNamespace.ToDisplayString();
        }

        if (document is not null)
        {
            AddRelatedTestNeedle(Path.GetFileNameWithoutExtension(document.FilePath ?? document.Name), needles);

            if (symbol is null)
            {
                var syntaxRoot = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
                var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
                if (syntaxRoot is not null && semanticModel is not null)
                {
                    foreach (var declaredSymbol in EnumerateDeclaredSymbols(syntaxRoot, semanticModel, cancellationToken))
                    {
                        if (declaredSymbol is INamedTypeSymbol or IMethodSymbol)
                        {
                            AddSymbolNeedles(declaredSymbol, needles);
                            if (string.IsNullOrEmpty(sourceNamespace)
                                && declaredSymbol.ContainingNamespace is not null
                                && !declaredSymbol.ContainingNamespace.IsGlobalNamespace)
                            {
                                sourceNamespace = declaredSymbol.ContainingNamespace.ToDisplayString();
                            }
                        }
                    }
                }
            }
        }

        return new RelatedTestsTargetContext(
            symbol,
            document,
            project?.Name ?? string.Empty,
            sourceNamespace,
            needles.OrderBy(needle => needle, StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private static async Task AddReferenceRelatedTestsAsync(
        ISymbol targetSymbol,
        Solution solution,
        IReadOnlyDictionary<ProjectId, string> testProjects,
        bool includeGeneratedCode,
        RelatedTestAccumulator accumulator,
        CancellationToken cancellationToken)
    {
        var references = await SymbolFinder.FindReferencesAsync(targetSymbol, solution, cancellationToken)
            .ConfigureAwait(false);

        foreach (var referencedSymbol in references)
        {
            foreach (var location in referencedSymbol.Locations)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (location.Location.SourceTree is null)
                {
                    continue;
                }

                var document = solution.GetDocument(location.Location.SourceTree);
                if (document is null
                    || !testProjects.TryGetValue(document.Project.Id, out var testFramework)
                    || (!includeGeneratedCode && IsGeneratedDocument(document)))
                {
                    continue;
                }

                var syntaxRoot = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
                var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
                if (syntaxRoot is null || semanticModel is null)
                {
                    continue;
                }

                var referenceSpan = CreateSpan(location.Location);
                var node = syntaxRoot.FindNode(location.Location.SourceSpan, getInnermostNodeForTie: true);
                var testInfo = FindRelatedTestSymbolInfo(node, semanticModel, cancellationToken);
                if (testInfo is null)
                {
                    continue;
                }

                var reasons = CreateReferenceMatchReasons(testInfo);
                var descriptor = CreateRelatedTestDescriptor(
                    testInfo,
                    document.Project,
                    testFramework,
                    reasons,
                    referenceSpan is null ? Array.Empty<SourceSpan>() : new[] { referenceSpan },
                    cancellationToken);
                if (descriptor is null)
                {
                    continue;
                }

                accumulator.Add(descriptor);
                if (accumulator.IsTruncated)
                {
                    return;
                }
            }
        }
    }

    private static async Task AddHeuristicRelatedTestsAsync(
        Solution solution,
        IReadOnlyDictionary<ProjectId, string> testProjects,
        RelatedTestsTargetContext target,
        bool includeGeneratedCode,
        RelatedTestAccumulator accumulator,
        CancellationToken cancellationToken)
    {
        if (target.Needles.Count == 0)
        {
            return;
        }

        foreach (var project in solution.Projects.Where(project => testProjects.ContainsKey(project.Id)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var testFramework = testProjects[project.Id];
            foreach (var document in project.Documents)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!includeGeneratedCode && IsGeneratedDocument(document))
                {
                    continue;
                }

                var syntaxRoot = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
                var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
                if (syntaxRoot is null || semanticModel is null)
                {
                    continue;
                }

                foreach (var method in syntaxRoot.DescendantNodes().OfType<MethodDeclarationSyntax>())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (semanticModel.GetDeclaredSymbol(method, cancellationToken) is not IMethodSymbol methodSymbol)
                    {
                        continue;
                    }

                    var testInfo = CreateRelatedTestSymbolInfo(methodSymbol);
                    if (testInfo is null)
                    {
                        continue;
                    }

                    if (!testInfo.HasKnownTestMethodAttribute
                        && !testInfo.HasKnownTestClassAttribute
                        && !IsExternallyVisibleTestMethod(methodSymbol))
                    {
                        continue;
                    }

                    var reasons = CreateHeuristicMatchReasons(testInfo, project, target);
                    if (reasons.Count == 0)
                    {
                        continue;
                    }

                    var descriptor = CreateRelatedTestDescriptor(
                        testInfo,
                        project,
                        testFramework,
                        reasons,
                        Array.Empty<SourceSpan>(),
                        cancellationToken);
                    if (descriptor is null)
                    {
                        continue;
                    }

                    accumulator.Add(descriptor);
                    if (accumulator.IsTruncated)
                    {
                        return;
                    }
                }
            }
        }
    }

    private static RelatedTestSymbolInfo? FindRelatedTestSymbolInfo(
        SyntaxNode? node,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        for (var current = node; current is not null; current = current.Parent)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (current is MethodDeclarationSyntax methodDeclaration)
            {
                return semanticModel.GetDeclaredSymbol(methodDeclaration, cancellationToken) is IMethodSymbol methodSymbol
                    ? CreateRelatedTestSymbolInfo(methodSymbol)
                    : null;
            }

            if (current is BaseTypeDeclarationSyntax typeDeclaration)
            {
                return semanticModel.GetDeclaredSymbol(typeDeclaration, cancellationToken) is INamedTypeSymbol typeSymbol
                    ? CreateRelatedTestSymbolInfo(typeSymbol)
                    : null;
            }
        }

        return null;
    }

    private static RelatedTestSymbolInfo? CreateRelatedTestSymbolInfo(IMethodSymbol methodSymbol)
    {
        if (methodSymbol.ContainingType is null)
        {
            return null;
        }

        return new RelatedTestSymbolInfo(
            methodSymbol,
            methodSymbol.ContainingType.Name,
            methodSymbol.Name,
            GetNamespace(methodSymbol.ContainingType),
            HasKnownTestMethodAttribute(methodSymbol),
            HasKnownTestClassAttribute(methodSymbol.ContainingType));
    }

    private static RelatedTestSymbolInfo CreateRelatedTestSymbolInfo(INamedTypeSymbol typeSymbol)
    {
        return new RelatedTestSymbolInfo(
            typeSymbol,
            typeSymbol.Name,
            string.Empty,
            GetNamespace(typeSymbol),
            hasKnownTestMethodAttribute: false,
            HasKnownTestClassAttribute(typeSymbol));
    }

    private static RelatedTestDescriptor? CreateRelatedTestDescriptor(
        RelatedTestSymbolInfo testInfo,
        Project project,
        string testFramework,
        IReadOnlyList<string> matchReasons,
        IReadOnlyList<SourceSpan> evidenceSpans,
        CancellationToken cancellationToken)
    {
        var testSymbol = CreateDescriptor(testInfo.Symbol, project, cancellationToken);
        if (testSymbol?.Span is null)
        {
            return null;
        }

        return new RelatedTestDescriptor
        {
            ProjectName = project.Name,
            TestFramework = testFramework,
            TestClass = testInfo.TestClass,
            TestMethod = testInfo.TestMethod,
            TestSymbol = testSymbol,
            Span = testSymbol.Span,
            MatchReasons = matchReasons,
            EvidenceSpans = evidenceSpans,
        };
    }

    private static IReadOnlyList<string> CreateReferenceMatchReasons(RelatedTestSymbolInfo testInfo)
    {
        var reasons = new List<string> { "ReferenceMatch" };
        if (testInfo.HasKnownTestMethodAttribute)
        {
            reasons.Add("KnownTestMethodAttribute");
        }
        else if (testInfo.HasKnownTestClassAttribute)
        {
            reasons.Add("KnownTestClassAttribute");
        }
        else
        {
            reasons.Add(string.IsNullOrEmpty(testInfo.TestMethod)
                ? "TestProjectTypeReference"
                : "TestProjectMethodReference");
        }

        return reasons;
    }

    private static IReadOnlyList<string> CreateHeuristicMatchReasons(
        RelatedTestSymbolInfo testInfo,
        Project project,
        RelatedTestsTargetContext target)
    {
        var reasons = new List<string>();
        var haystack = string.Join(
            " ",
            new[]
            {
                testInfo.TestClass,
                testInfo.TestMethod,
                testInfo.Symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
            });

        if (!TryFindNeedleMatch(haystack, target.Needles, out var matchedNeedle))
        {
            return reasons;
        }

        reasons.Add("NameMatch:" + matchedNeedle);
        if (NamespaceMatches(testInfo.Namespace, target.Namespace))
        {
            reasons.Add("NamespaceMatch");
        }

        if (ProjectNameMatches(project.Name, target.ProjectName))
        {
            reasons.Add("ProjectNameMatch");
        }

        if (testInfo.HasKnownTestMethodAttribute)
        {
            reasons.Add("KnownTestMethodAttribute");
        }
        else if (testInfo.HasKnownTestClassAttribute)
        {
            reasons.Add("KnownTestClassAttribute");
        }
        else
        {
            reasons.Add("HeuristicTestProjectMethod");
        }

        return reasons;
    }

    private static void AddSymbolNeedles(ISymbol symbol, ISet<string> needles)
    {
        AddRelatedTestNeedle(symbol.Name, needles);
        if (symbol.ContainingType is not null)
        {
            AddRelatedTestNeedle(symbol.ContainingType.Name, needles);
            AddRelatedTestNeedle(symbol.ContainingType.Name + symbol.Name, needles);
        }
    }

    private static void AddRelatedTestNeedle(string? value, ISet<string> needles)
    {
        var trimmed = value?.Trim();
        if (trimmed is null || trimmed.Length == 0)
        {
            return;
        }

        var normalized = NormalizeIdentifierForMatch(trimmed);
        if (normalized.Length < 3)
        {
            return;
        }

        needles.Add(trimmed);
    }

    private static bool IsLikelyTestProject(Project project)
    {
        if (ContainsTestProjectToken(project.Name) || ContainsTestProjectToken(project.FilePath))
        {
            return true;
        }

        return project.MetadataReferences.Any(reference =>
        {
            var display = reference.Display ?? string.Empty;
            return ContainsTestFrameworkReference(display);
        });
    }

    private static string DetectTestFramework(Project project)
    {
        var evidence = string.Join(
            " ",
            project.MetadataReferences.Select(reference => reference.Display ?? string.Empty)
                .Concat(new[] { project.Name, project.FilePath ?? string.Empty }));

        if (evidence.IndexOf("xunit", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "xUnit";
        }

        if (evidence.IndexOf("nunit", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "NUnit";
        }

        if (evidence.IndexOf("mstest", StringComparison.OrdinalIgnoreCase) >= 0
            || evidence.IndexOf("Microsoft.VisualStudio.TestTools", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "MSTest";
        }

        return "Unknown";
    }

    private static bool ContainsTestProjectToken(string? value)
    {
        var trimmed = value?.Trim();
        if (trimmed is null || trimmed.Length == 0)
        {
            return false;
        }

        foreach (var token in SplitNameTokens(trimmed))
        {
            if (string.Equals(token, "test", StringComparison.OrdinalIgnoreCase)
                || string.Equals(token, "tests", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return trimmed.EndsWith("Tests", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsTestFrameworkReference(string value)
    {
        return value.IndexOf("xunit", StringComparison.OrdinalIgnoreCase) >= 0
            || value.IndexOf("nunit", StringComparison.OrdinalIgnoreCase) >= 0
            || value.IndexOf("mstest", StringComparison.OrdinalIgnoreCase) >= 0
            || value.IndexOf("Microsoft.VisualStudio.TestTools", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool HasKnownTestMethodAttribute(IMethodSymbol symbol)
    {
        return HasAnyAttribute(
            symbol,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Fact",
                "Theory",
                "Test",
                "TestCase",
                "TestCaseSource",
                "TestMethod",
                "DataTestMethod",
            });
    }

    private static bool HasKnownTestClassAttribute(INamedTypeSymbol symbol)
    {
        return HasAnyAttribute(
            symbol,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "TestClass",
                "TestFixture",
                "Collection",
            });
    }

    private static bool HasAnyAttribute(ISymbol symbol, ISet<string> attributeNames)
    {
        foreach (var attribute in symbol.GetAttributes())
        {
            var name = attribute.AttributeClass?.Name ?? string.Empty;
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var normalized = name.EndsWith("Attribute", StringComparison.Ordinal)
                ? name.Substring(0, name.Length - "Attribute".Length)
                : name;
            if (attributeNames.Contains(normalized))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsExternallyVisibleTestMethod(IMethodSymbol methodSymbol)
    {
        return methodSymbol.DeclaredAccessibility is Accessibility.Public
            or Accessibility.Internal
            or Accessibility.Protected
            or Accessibility.ProtectedOrInternal;
    }

    private static bool TryFindNeedleMatch(
        string value,
        IReadOnlyList<string> needles,
        out string matchedNeedle)
    {
        var normalizedValue = NormalizeIdentifierForMatch(value);
        foreach (var needle in needles)
        {
            var normalizedNeedle = NormalizeIdentifierForMatch(needle);
            if (normalizedNeedle.Length >= 3
                && normalizedValue.IndexOf(normalizedNeedle, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                matchedNeedle = needle;
                return true;
            }
        }

        matchedNeedle = string.Empty;
        return false;
    }

    private static bool NamespaceMatches(string testNamespace, string targetNamespace)
    {
        if (string.IsNullOrWhiteSpace(testNamespace) || string.IsNullOrWhiteSpace(targetNamespace))
        {
            return false;
        }

        var normalizedTest = NormalizeIdentifierForMatch(testNamespace);
        var normalizedTarget = NormalizeIdentifierForMatch(targetNamespace);
        if (normalizedTarget.Length < 4)
        {
            return false;
        }

        if (normalizedTest.IndexOf(normalizedTarget, StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return true;
        }

        var lastSegment = targetNamespace.Split('.').LastOrDefault();
        return !string.IsNullOrWhiteSpace(lastSegment)
            && lastSegment.Length >= 4
            && normalizedTest.IndexOf(NormalizeIdentifierForMatch(lastSegment), StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool ProjectNameMatches(string testProjectName, string targetProjectName)
    {
        if (string.IsNullOrWhiteSpace(testProjectName) || string.IsNullOrWhiteSpace(targetProjectName))
        {
            return false;
        }

        var normalizedTest = NormalizeIdentifierForMatch(testProjectName);
        var normalizedTarget = NormalizeIdentifierForMatch(targetProjectName);
        if (normalizedTest.EndsWith("tests", StringComparison.OrdinalIgnoreCase))
        {
            normalizedTest = normalizedTest.Substring(0, normalizedTest.Length - "tests".Length);
        }

        return normalizedTest.Length > 0
            && normalizedTarget.Length > 0
            && (normalizedTest.IndexOf(normalizedTarget, StringComparison.OrdinalIgnoreCase) >= 0
                || normalizedTarget.IndexOf(normalizedTest, StringComparison.OrdinalIgnoreCase) >= 0);
    }

    private static string NormalizeIdentifierForMatch(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var chars = value
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray();
        return new string(chars);
    }

    private static IEnumerable<string> SplitNameTokens(string value)
    {
        return value.Split(new[] { '.', '-', '_', '/', '\\', ' ', ':' }, StringSplitOptions.RemoveEmptyEntries);
    }

    private static string GetNamespace(ISymbol symbol)
    {
        return symbol.ContainingNamespace is null || symbol.ContainingNamespace.IsGlobalNamespace
            ? string.Empty
            : symbol.ContainingNamespace.ToDisplayString();
    }

    private static string? ValidateCallGraphRequest(CallGraphRequest request)
    {
        if (request.MaxDepth is < 1 or > 10)
        {
            return "MaxDepth must be between 1 and 10.";
        }

        if (request.MaxResults is < 1 or > 1000)
        {
            return "MaxResults must be between 1 and 1000.";
        }

        return null;
    }

    private static string? ValidateSymbolImpactRequest(SymbolImpactRequest request)
    {
        if (request.MaxDepth is < 1 or > 10)
        {
            return "MaxDepth must be between 1 and 10.";
        }

        if (request.MaxResults is < 1 or > 10000)
        {
            return "MaxResults must be between 1 and 10000.";
        }

        if (request.MaxProjects is < 1 or > 200)
        {
            return "MaxProjects must be between 1 and 200.";
        }

        if (request.MaxFiles is < 1 or > 200)
        {
            return "MaxFiles must be between 1 and 200.";
        }

        if (request.MaxContainingTypes is < 1 or > 200)
        {
            return "MaxContainingTypes must be between 1 and 200.";
        }

        return null;
    }

    private static string? ValidateSourceContextRequest(SourceContextRequest request, bool requireSymbol)
    {
        if (request.ContextLines is < 0 or > 50)
        {
            return "ContextLines must be between 0 and 50.";
        }

        if (request.MaxChars is < 1 or > 200000)
        {
            return "MaxChars must be between 1 and 200000.";
        }

        if (request.MaxSnippets is < 1 or > 20)
        {
            return "MaxSnippets must be between 1 and 20.";
        }

        if (requireSymbol)
        {
            if (request.SymbolKey is null && request.Position is null)
            {
                return "Provide either SymbolKey or source position.";
            }
        }
        else if (request.Position is null)
        {
            return "Source position is required.";
        }

        if (request.Position is not null
            && (string.IsNullOrWhiteSpace(request.Position.FilePath)
                || request.Position.StartLine <= 0
                || request.Position.StartColumn <= 0))
        {
            return "Source position requires filePath, one-based line, and one-based column.";
        }

        return null;
    }

    private static void AddDerivedTypeDescriptor(
        INamedTypeSymbol symbol,
        INamedTypeSymbol targetType,
        Solution solution,
        DerivedTypesRequest request,
        List<DerivedTypeDescriptor> items,
        HashSet<ISymbol> seen,
        CancellationToken cancellationToken)
    {
        if (!seen.Add(symbol))
        {
            return;
        }

        var descriptor = CreateDescriptor(symbol, solution, cancellationToken);
        if (descriptor?.Span is null)
        {
            return;
        }

        if (!request.IncludeGeneratedCode && IsGeneratedPath(descriptor.Span.FilePath))
        {
            return;
        }

        var depth = CalculateDerivationDepth(symbol, targetType);
        items.Add(new DerivedTypeDescriptor
        {
            Symbol = descriptor,
            BaseType = symbol.BaseType?.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat) ?? string.Empty,
            Depth = depth,
            IsDirect = depth == 1,
        });
    }

    private static int CalculateDerivationDepth(INamedTypeSymbol candidate, INamedTypeSymbol targetType)
    {
        var classDepth = CalculateClassDerivationDepth(candidate, targetType);
        var interfaceDepth = CalculateInterfaceDerivationDepth(candidate, targetType, new HashSet<ISymbol>(SymbolEqualityComparer.Default));
        if (classDepth <= 0)
        {
            return interfaceDepth <= 0 ? 0 : interfaceDepth;
        }

        if (interfaceDepth <= 0)
        {
            return classDepth;
        }

        return Math.Min(classDepth, interfaceDepth);
    }

    private static int CalculateClassDerivationDepth(INamedTypeSymbol candidate, INamedTypeSymbol targetType)
    {
        var depth = 0;
        for (var current = candidate.BaseType; current is not null; current = current.BaseType)
        {
            depth++;
            if (IsSameNamedType(current, targetType))
            {
                return depth;
            }
        }

        return 0;
    }

    private static int CalculateInterfaceDerivationDepth(
        INamedTypeSymbol candidate,
        INamedTypeSymbol targetType,
        HashSet<ISymbol> visited)
    {
        if (!visited.Add(candidate))
        {
            return 0;
        }

        var best = 0;
        foreach (var @interface in candidate.Interfaces)
        {
            if (IsSameNamedType(@interface, targetType))
            {
                return 1;
            }

            var nestedDepth = CalculateInterfaceDerivationDepth(@interface, targetType, visited);
            if (nestedDepth > 0)
            {
                var candidateDepth = nestedDepth + 1;
                best = best == 0 ? candidateDepth : Math.Min(best, candidateDepth);
            }
        }

        if (candidate.BaseType is not null)
        {
            var baseDepth = CalculateInterfaceDerivationDepth(candidate.BaseType, targetType, visited);
            if (baseDepth > 0)
            {
                var candidateDepth = baseDepth + 1;
                best = best == 0 ? candidateDepth : Math.Min(best, candidateDepth);
            }
        }

        return best;
    }

    private static bool IsSameNamedType(INamedTypeSymbol candidate, INamedTypeSymbol targetType)
    {
        return SymbolEqualityComparer.Default.Equals(candidate.OriginalDefinition, targetType.OriginalDefinition)
            || SymbolEqualityComparer.Default.Equals(candidate, targetType)
            || string.Equals(
                candidate.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                targetType.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                StringComparison.Ordinal);
    }

    private static bool DirectlyImplementsInterface(INamedTypeSymbol candidate, INamedTypeSymbol targetInterface)
    {
        return candidate.Interfaces.Any(@interface => IsSameNamedType(@interface, targetInterface));
    }

    private static ProjectGraphNode CreateProjectGraphNode(
        Project project,
        ProjectGraphRequest request,
        List<string> diagnostics)
    {
        var metadataReferences = request.IncludeMetadataReferences
            ? project.MetadataReferences
                .Select(reference => reference.Display ?? string.Empty)
                .Where(display => !string.IsNullOrWhiteSpace(display))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(display => display, StringComparer.OrdinalIgnoreCase)
                .Take(request.MaxMetadataReferencesPerProject)
                .ToArray()
            : Array.Empty<string>();

        if (request.IncludeMetadataReferences
            && project.MetadataReferences.Count() > request.MaxMetadataReferencesPerProject)
        {
            diagnostics.Add($"Metadata references for project '{project.Name}' were truncated at MaxMetadataReferencesPerProject={request.MaxMetadataReferencesPerProject}.");
        }

        return new ProjectGraphNode
        {
            ProjectId = project.Id.Id.ToString(),
            ProjectName = project.Name,
            AssemblyName = project.AssemblyName ?? string.Empty,
            Language = project.Language,
            FilePath = project.FilePath ?? string.Empty,
            TargetFramework = ReadTargetFramework(project.FilePath),
            DocumentCount = project.DocumentIds.Count,
            ProjectReferenceCount = project.ProjectReferences.Count(),
            MetadataReferenceCount = project.MetadataReferences.Count(),
            MetadataReferences = metadataReferences,
        };
    }

    private static ProjectGraphEdge? CreateProjectGraphEdge(
        Project sourceProject,
        ProjectReference reference,
        Solution solution,
        ISet<string> returnedProjectIds)
    {
        var targetProject = solution.GetProject(reference.ProjectId);
        if (targetProject is null)
        {
            return null;
        }

        return new ProjectGraphEdge
        {
            SourceProjectId = sourceProject.Id.Id.ToString(),
            SourceProjectName = sourceProject.Name,
            TargetProjectId = targetProject.Id.Id.ToString(),
            TargetProjectName = targetProject.Name,
            TargetProjectIncluded = returnedProjectIds.Contains(targetProject.Id.Id.ToString()),
            Kind = ProjectGraphEdgeKind.ProjectReference,
        };
    }

    private static string ReadTargetFramework(string? projectFilePath)
    {
        if (string.IsNullOrWhiteSpace(projectFilePath) || !File.Exists(projectFilePath))
        {
            return string.Empty;
        }

        try
        {
            var document = XDocument.Load(projectFilePath);
            var targetFramework = document.Descendants()
                .FirstOrDefault(element => string.Equals(element.Name.LocalName, "TargetFramework", StringComparison.OrdinalIgnoreCase))
                ?.Value
                ?.Trim();
            if (!string.IsNullOrWhiteSpace(targetFramework))
            {
                return targetFramework!;
            }

            return document.Descendants()
                .FirstOrDefault(element => string.Equals(element.Name.LocalName, "TargetFrameworks", StringComparison.OrdinalIgnoreCase))
                ?.Value
                ?.Trim() ?? string.Empty;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            return string.Empty;
        }
    }

    private static SymbolDescription CreateSymbolDescription(
        ISymbol symbol,
        SymbolDescriptor descriptor,
        CancellationToken cancellationToken)
    {
        var namedType = symbol as INamedTypeSymbol;
        var method = symbol as IMethodSymbol;
        var property = symbol as IPropertySymbol;
        var delegateInvoke = namedType?.DelegateInvokeMethod;

        return new SymbolDescription
        {
            Symbol = descriptor,
            DisplayString = symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
            DocumentationCommentId = symbol.GetDocumentationCommentId() ?? string.Empty,
            Accessibility = symbol.DeclaredAccessibility.ToString(),
            ContainingAssembly = symbol.ContainingAssembly?.Name ?? string.Empty,
            BaseType = namedType?.BaseType?.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat) ?? string.Empty,
            Interfaces = (namedType?.AllInterfaces ?? ImmutableArray<INamedTypeSymbol>.Empty)
                .Select(item => item.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat))
                .ToArray(),
            Attributes = symbol.GetAttributes()
                .Select(attribute => attribute.AttributeClass?.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat) ?? string.Empty)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.Ordinal)
                .ToArray(),
            Parameters = (method?.Parameters ?? property?.Parameters ?? delegateInvoke?.Parameters ?? ImmutableArray<IParameterSymbol>.Empty)
                .Select(parameter => parameter.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat))
                .ToArray(),
            TypeParameters = (method?.TypeParameters ?? namedType?.TypeParameters ?? ImmutableArray<ITypeParameterSymbol>.Empty)
                .Select(parameter => parameter.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat))
                .ToArray(),
            IsStatic = symbol.IsStatic,
            IsAbstract = symbol.IsAbstract,
            IsVirtual = symbol.IsVirtual,
            IsOverride = symbol.IsOverride,
            IsSealed = symbol.IsSealed,
            IsPartial = IsPartialSymbol(symbol, cancellationToken),
        };
    }

    private static bool IsPartialSymbol(ISymbol symbol, CancellationToken cancellationToken)
    {
        foreach (var declaration in symbol.DeclaringSyntaxReferences)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var syntax = declaration.GetSyntax(cancellationToken);
            switch (syntax)
            {
                case BaseTypeDeclarationSyntax typeDeclaration:
                    if (typeDeclaration.Modifiers.Any(Microsoft.CodeAnalysis.CSharp.SyntaxKind.PartialKeyword))
                    {
                        return true;
                    }

                    break;
                case MethodDeclarationSyntax methodDeclaration:
                    if (methodDeclaration.Modifiers.Any(Microsoft.CodeAnalysis.CSharp.SyntaxKind.PartialKeyword))
                    {
                        return true;
                    }

                    break;
                case PropertyDeclarationSyntax propertyDeclaration:
                    if (propertyDeclaration.Modifiers.Any(Microsoft.CodeAnalysis.CSharp.SyntaxKind.PartialKeyword))
                    {
                        return true;
                    }

                    break;
            }
        }

        return false;
    }

    private static WorkspaceQueryResult<DocumentSymbolNode> CreateDocumentSymbolNodes(
        SyntaxNode syntaxRoot,
        SemanticModel semanticModel,
        Project project,
        int maxResults,
        CancellationToken cancellationToken)
    {
        var symbolIds = new Dictionary<ISymbol, string>(SymbolEqualityComparer.Default);
        var symbolDepths = new Dictionary<ISymbol, int>(SymbolEqualityComparer.Default);
        var nodes = new List<DocumentSymbolNode>();
        var diagnostics = new List<string>();
        var sequence = 0;

        foreach (var syntaxNode in syntaxRoot.DescendantNodes())
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var symbol in GetDeclaredSymbolsForNode(syntaxNode, semanticModel, cancellationToken))
            {
                var descriptor = CreateDescriptor(symbol, project, cancellationToken);
                if (descriptor?.Span is null)
                {
                    continue;
                }

                var parentSymbol = FindParentDeclaredSymbol(syntaxNode.Parent, semanticModel, cancellationToken);
                var parentId = parentSymbol is not null && symbolIds.TryGetValue(parentSymbol, out var existingParentId)
                    ? existingParentId
                    : string.Empty;
                var depth = parentSymbol is not null && symbolDepths.TryGetValue(parentSymbol, out var existingDepth)
                    ? existingDepth + 1
                    : 0;
                var id = "s" + (++sequence).ToString(System.Globalization.CultureInfo.InvariantCulture);
                symbolIds[symbol] = id;
                symbolDepths[symbol] = depth;
                nodes.Add(new DocumentSymbolNode
                {
                    Id = id,
                    ParentId = parentId,
                    Depth = depth,
                    Symbol = descriptor,
                    Detail = symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
                });

                if (nodes.Count >= maxResults)
                {
                    diagnostics.Add($"Document symbols were truncated at MaxResults={maxResults}.");
                    return Success(nodes.ToArray(), diagnostics, isPartial: true);
                }
            }
        }

        return Success(nodes.ToArray(), diagnostics, isPartial: false);
    }

    private static IEnumerable<ISymbol> GetDeclaredSymbolsForNode(
        SyntaxNode node,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        switch (node)
        {
            case BaseTypeDeclarationSyntax:
            case DelegateDeclarationSyntax:
            case MethodDeclarationSyntax:
            case ConstructorDeclarationSyntax:
            case DestructorDeclarationSyntax:
            case OperatorDeclarationSyntax:
            case ConversionOperatorDeclarationSyntax:
            case PropertyDeclarationSyntax:
            case EventDeclarationSyntax:
            case NamespaceDeclarationSyntax:
            case FileScopedNamespaceDeclarationSyntax:
            case AccessorDeclarationSyntax:
            case LocalFunctionStatementSyntax:
                var symbol = semanticModel.GetDeclaredSymbol(node, cancellationToken);
                if (symbol is not null)
                {
                    yield return symbol;
                }

                break;
            case FieldDeclarationSyntax fieldDeclaration:
                foreach (var variable in fieldDeclaration.Declaration.Variables)
                {
                    var fieldSymbol = semanticModel.GetDeclaredSymbol(variable, cancellationToken);
                    if (fieldSymbol is not null)
                    {
                        yield return fieldSymbol;
                    }
                }

                break;
            case EventFieldDeclarationSyntax eventFieldDeclaration:
                foreach (var variable in eventFieldDeclaration.Declaration.Variables)
                {
                    var eventSymbol = semanticModel.GetDeclaredSymbol(variable, cancellationToken);
                    if (eventSymbol is not null)
                    {
                        yield return eventSymbol;
                    }
                }

                break;
        }
    }

    private static ISymbol? FindParentDeclaredSymbol(
        SyntaxNode? node,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        for (var current = node; current is not null; current = current.Parent)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var symbol = GetDeclaredSymbolsForNode(current, semanticModel, cancellationToken).FirstOrDefault();
            if (symbol is not null)
            {
                return symbol;
            }
        }

        return null;
    }


    private static async Task<ISymbol?> FindContainingDeclaredSymbolAsync(
        Document document,
        TextSpan sourceSpan,
        CancellationToken cancellationToken)
    {
        var syntaxRoot = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (syntaxRoot is null || semanticModel is null)
        {
            return null;
        }

        var node = syntaxRoot.FindNode(sourceSpan);
        for (var current = node; current is not null; current = current.Parent)
        {
            cancellationToken.ThrowIfCancellationRequested();
            switch (current)
            {
                case BaseMethodDeclarationSyntax:
                case AccessorDeclarationSyntax:
                case PropertyDeclarationSyntax:
                case EventDeclarationSyntax:
                case FieldDeclarationSyntax:
                case LocalFunctionStatementSyntax:
                case BaseTypeDeclarationSyntax:
                    var symbol = semanticModel.GetDeclaredSymbol(current, cancellationToken);
                    if (symbol is not null)
                    {
                        return symbol;
                    }

                    break;
            }
        }

        return null;
    }

    private static async Task<SymbolImpactSiteCollectionResult> CollectSymbolImpactSitesAsync(
        ISymbol targetSymbol,
        Solution solution,
        SymbolImpactRequest request,
        CancellationToken cancellationToken)
    {
        var sites = new List<SymbolImpactSite>();
        var diagnostics = new List<string>();
        var isPartial = false;
        var expandedSymbols = new HashSet<string>(StringComparer.Ordinal);
        var queuedSymbols = new HashSet<string>(StringComparer.Ordinal)
        {
            CreateCallGraphSymbolIdentity(targetSymbol),
        };
        var frontier = new List<ISymbol>
        {
            targetSymbol,
        };

        if (request.MaxDepth > 1)
        {
            diagnostics.Add(
                "Impact analysis MaxDepth>1 aggregates direct references and recursive caller references. Use find_csharp_callers when individual propagation edges are required.");
        }

        for (var depth = 1; depth <= request.MaxDepth && frontier.Count > 0; depth++)
        {
            var nextFrontier = new List<ISymbol>();
            foreach (var currentSymbol in frontier)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!expandedSymbols.Add(CreateCallGraphSymbolIdentity(currentSymbol)))
                {
                    continue;
                }

                var references = await SymbolFinder.FindReferencesAsync(currentSymbol, solution, cancellationToken)
                    .ConfigureAwait(false);

                foreach (var referencedSymbol in references)
                {
                    foreach (var location in referencedSymbol.Locations)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (location.Location.SourceTree is null)
                        {
                            continue;
                        }

                        var document = solution.GetDocument(location.Location.SourceTree);
                        if (document is null)
                        {
                            continue;
                        }

                        if (!request.IncludeGeneratedCode && IsGeneratedDocument(document))
                        {
                            continue;
                        }

                        var span = CreateSpan(location.Location);
                        if (span is null)
                        {
                            continue;
                        }

                        var role = await ClassifyReferenceRoleAsync(document, location.Location.SourceSpan, cancellationToken)
                            .ConfigureAwait(false);
                        var containingSymbol = await FindContainingDeclaredSymbolAsync(
                                document,
                                location.Location.SourceSpan,
                                cancellationToken)
                            .ConfigureAwait(false);

                        sites.Add(new SymbolImpactSite
                        {
                            ProjectName = document.Project.Name,
                            FilePath = span.FilePath,
                            ContainingType = GetContainingTypeDisplay(containingSymbol),
                            Role = role,
                            Depth = depth,
                        });

                        if (depth < request.MaxDepth && containingSymbol is not null)
                        {
                            var descriptor = CreateDescriptor(containingSymbol, solution, cancellationToken);
                            if (descriptor?.Span is not null)
                            {
                                var containingIdentity = CreateCallGraphSymbolIdentity(containingSymbol);
                                if (queuedSymbols.Add(containingIdentity))
                                {
                                    nextFrontier.Add(containingSymbol);
                                }
                            }
                        }

                        if (sites.Count >= request.MaxResults)
                        {
                            diagnostics.Add($"Impact analysis was truncated at MaxResults={request.MaxResults} before completing MaxDepth={request.MaxDepth}; totalReferences in the returned summary is the processed reference count, not the full solution estimate.");
                            isPartial = true;
                            return SymbolImpactSiteCollectionResult.Success(sites, diagnostics, isPartial);
                        }
                    }
                }
            }

            frontier = nextFrontier;
        }

        return SymbolImpactSiteCollectionResult.Success(sites, diagnostics, isPartial);
    }

    private static async Task<bool> VisitDirectCallerEdgesAsync(
        ISymbol targetSymbol,
        SymbolDescriptor targetDescriptor,
        Solution solution,
        bool includeGeneratedCode,
        int depth,
        Func<CallGraphTraversalEdge, bool> visit,
        CancellationToken cancellationToken)
    {
        var references = await SymbolFinder.FindReferencesAsync(targetSymbol, solution, cancellationToken)
            .ConfigureAwait(false);

        foreach (var referencedSymbol in references)
        {
            foreach (var location in referencedSymbol.Locations)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (location.Location.SourceTree is null)
                {
                    continue;
                }

                var document = solution.GetDocument(location.Location.SourceTree);
                if (document is null || (!includeGeneratedCode && IsGeneratedDocument(document)))
                {
                    continue;
                }

                var callerSymbol = await FindContainingDeclaredSymbolAsync(
                        document,
                        location.Location.SourceSpan,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (callerSymbol is null)
                {
                    continue;
                }

                var callerDescriptor = CreateDescriptor(callerSymbol, solution, cancellationToken);
                var span = CreateSpan(location.Location);
                if (callerDescriptor?.Span is null || span is null)
                {
                    continue;
                }

                var role = await ClassifyReferenceRoleAsync(document, location.Location.SourceSpan, cancellationToken)
                    .ConfigureAwait(false);
                var edgeKind = MapReferenceRoleToCallGraphKind(role, targetSymbol);
                if (edgeKind == CallGraphEdgeKind.Unknown)
                {
                    continue;
                }

                if (!visit(new CallGraphTraversalEdge(
                        callerSymbol,
                        callerDescriptor,
                        new CallGraphEdge
                        {
                            Source = callerDescriptor,
                            Target = targetDescriptor,
                            Span = span,
                            Kind = edgeKind,
                            Depth = depth,
                        })))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static async Task<bool> VisitDirectCalleeEdgesAsync(
        ISymbol sourceSymbol,
        SymbolDescriptor sourceDescriptor,
        Solution solution,
        bool includeGeneratedCode,
        int depth,
        List<string> diagnostics,
        Func<CallGraphTraversalEdge, bool> visit,
        CancellationToken cancellationToken)
    {
        foreach (var declaration in sourceSymbol.DeclaringSyntaxReferences)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var syntax = await declaration.GetSyntaxAsync(cancellationToken).ConfigureAwait(false);
            var document = solution.GetDocument(syntax.SyntaxTree);
            if (document is null || (!includeGeneratedCode && IsGeneratedDocument(document)))
            {
                continue;
            }

            var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
            var syntaxRoot = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
            if (semanticModel is null || syntaxRoot is null)
            {
                diagnostics.Add($"Document '{document.FilePath ?? document.Name}' has no semantic model or syntax root.");
                continue;
            }

            foreach (var edge in CreateCalleeEdges(
                         syntax,
                         syntaxRoot,
                         semanticModel,
                         solution,
                         sourceDescriptor,
                         includeGeneratedCode,
                         depth,
                         cancellationToken))
            {
                if (!visit(edge))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static IEnumerable<CallGraphTraversalEdge> CreateCalleeEdges(
        SyntaxNode declarationSyntax,
        SyntaxNode syntaxRoot,
        SemanticModel semanticModel,
        Solution solution,
        SymbolDescriptor sourceDescriptor,
        bool includeGeneratedCode,
        int depth,
        CancellationToken cancellationToken)
    {
        foreach (var node in declarationSyntax.DescendantNodes())
        {
            cancellationToken.ThrowIfCancellationRequested();
            switch (node)
            {
                case InvocationExpressionSyntax invocation:
                    foreach (var edge in CreateInvocationCalleeEdge(invocation, semanticModel, solution, sourceDescriptor, includeGeneratedCode, depth, cancellationToken))
                    {
                        yield return edge;
                    }

                    break;
                case ObjectCreationExpressionSyntax objectCreation:
                    foreach (var edge in CreateObjectCreationCalleeEdge(objectCreation, semanticModel, solution, sourceDescriptor, includeGeneratedCode, depth, cancellationToken))
                    {
                        yield return edge;
                    }

                    break;
                case MemberAccessExpressionSyntax memberAccess
                    when !IsInvocationExpressionTarget(memberAccess):
                    foreach (var edge in CreateMemberAccessCalleeEdge(memberAccess, syntaxRoot, semanticModel, solution, sourceDescriptor, includeGeneratedCode, depth, cancellationToken))
                    {
                        yield return edge;
                    }

                    break;
                case IdentifierNameSyntax identifier
                    when !IsMemberAccessName(identifier) && !IsInvocationExpressionTarget(identifier):
                    foreach (var edge in CreateIdentifierCalleeEdge(identifier, syntaxRoot, semanticModel, solution, sourceDescriptor, includeGeneratedCode, depth, cancellationToken))
                    {
                        yield return edge;
                    }

                    break;
            }
        }
    }

    private static IEnumerable<CallGraphTraversalEdge> CreateInvocationCalleeEdge(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        Solution solution,
        SymbolDescriptor sourceDescriptor,
        bool includeGeneratedCode,
        int depth,
        CancellationToken cancellationToken)
    {
        var symbol = semanticModel.GetSymbolInfo(invocation, cancellationToken).Symbol
            ?? semanticModel.GetSymbolInfo(invocation.Expression, cancellationToken).CandidateSymbols.FirstOrDefault();
        return CreateSingleCalleeEdge(
            symbol,
            invocation.Expression.GetLocation(),
            solution,
            sourceDescriptor,
            CallGraphEdgeKind.Invocation,
            includeGeneratedCode,
            depth,
            cancellationToken);
    }

    private static IEnumerable<CallGraphTraversalEdge> CreateObjectCreationCalleeEdge(
        ObjectCreationExpressionSyntax objectCreation,
        SemanticModel semanticModel,
        Solution solution,
        SymbolDescriptor sourceDescriptor,
        bool includeGeneratedCode,
        int depth,
        CancellationToken cancellationToken)
    {
        var symbol = semanticModel.GetSymbolInfo(objectCreation, cancellationToken).Symbol
            ?? semanticModel.GetSymbolInfo(objectCreation.Type, cancellationToken).CandidateSymbols.FirstOrDefault();
        return CreateSingleCalleeEdge(
            symbol,
            objectCreation.Type.GetLocation(),
            solution,
            sourceDescriptor,
            CallGraphEdgeKind.ObjectCreation,
            includeGeneratedCode,
            depth,
            cancellationToken);
    }

    private static IEnumerable<CallGraphTraversalEdge> CreateMemberAccessCalleeEdge(
        MemberAccessExpressionSyntax memberAccess,
        SyntaxNode syntaxRoot,
        SemanticModel semanticModel,
        Solution solution,
        SymbolDescriptor sourceDescriptor,
        bool includeGeneratedCode,
        int depth,
        CancellationToken cancellationToken)
    {
        var symbol = semanticModel.GetSymbolInfo(memberAccess.Name, cancellationToken).Symbol;
        return CreateSingleCalleeEdge(
            symbol,
            memberAccess.Name.GetLocation(),
            solution,
            sourceDescriptor,
            MapSymbolAccessKind(symbol, ReferenceRoleClassifier.Classify(syntaxRoot, memberAccess.Name.Span)),
            includeGeneratedCode,
            depth,
            cancellationToken);
    }

    private static IEnumerable<CallGraphTraversalEdge> CreateIdentifierCalleeEdge(
        IdentifierNameSyntax identifier,
        SyntaxNode syntaxRoot,
        SemanticModel semanticModel,
        Solution solution,
        SymbolDescriptor sourceDescriptor,
        bool includeGeneratedCode,
        int depth,
        CancellationToken cancellationToken)
    {
        var symbol = semanticModel.GetSymbolInfo(identifier, cancellationToken).Symbol;
        return CreateSingleCalleeEdge(
            symbol,
            identifier.GetLocation(),
            solution,
            sourceDescriptor,
            MapSymbolAccessKind(symbol, ReferenceRoleClassifier.Classify(syntaxRoot, identifier.Span)),
            includeGeneratedCode,
            depth,
            cancellationToken);
    }

    private static IEnumerable<CallGraphTraversalEdge> CreateSingleCalleeEdge(
        ISymbol? targetSymbol,
        Location location,
        Solution solution,
        SymbolDescriptor sourceDescriptor,
        CallGraphEdgeKind kind,
        bool includeGeneratedCode,
        int depth,
        CancellationToken cancellationToken)
    {
        if (targetSymbol is null || kind == CallGraphEdgeKind.Unknown)
        {
            yield break;
        }

        if (targetSymbol is IMethodSymbol method && method.ReducedFrom is not null)
        {
            targetSymbol = method.ReducedFrom;
        }

        var targetDescriptor = CreateDescriptor(targetSymbol, solution, cancellationToken);
        var span = CreateSpan(location);
        if (targetDescriptor?.Span is null || span is null)
        {
            yield break;
        }

        if (!includeGeneratedCode && IsGeneratedPath(targetDescriptor.Span.FilePath))
        {
            yield break;
        }

        yield return new CallGraphTraversalEdge(
            targetSymbol,
            targetDescriptor,
            new CallGraphEdge
            {
                Source = sourceDescriptor,
                Target = targetDescriptor,
                Span = span,
                Kind = kind,
                Depth = depth,
            });
    }

    private static CallGraphEdgeKind MapReferenceRoleToCallGraphKind(ReferenceRole role, ISymbol targetSymbol)
    {
        return MapSymbolAccessKind(targetSymbol, role);
    }

    private static CallGraphEdgeKind MapSymbolAccessKind(ISymbol? symbol, ReferenceRole role)
    {
        if (symbol is null)
        {
            return CallGraphEdgeKind.Unknown;
        }

        if (role == ReferenceRole.Invocation)
        {
            return symbol.Kind == SymbolKind.Method || symbol.Kind == SymbolKind.NamedType
                ? CallGraphEdgeKind.Invocation
                : CallGraphEdgeKind.Unknown;
        }

        return symbol.Kind switch
        {
            SymbolKind.Method => CallGraphEdgeKind.Invocation,
            SymbolKind.Property => role == ReferenceRole.Write ? CallGraphEdgeKind.PropertyWrite : CallGraphEdgeKind.PropertyRead,
            SymbolKind.Event => CallGraphEdgeKind.EventReference,
            SymbolKind.Field => role == ReferenceRole.Write ? CallGraphEdgeKind.FieldWrite : CallGraphEdgeKind.FieldRead,
            _ => CallGraphEdgeKind.Unknown,
        };
    }

    private static string CreateCallGraphSymbolIdentity(ISymbol symbol)
    {
        var symbolKey = CreateSymbolKey(symbol);
        if (symbolKey is not null)
        {
            return symbolKey.Value;
        }

        var sourceLocation = symbol.Locations.FirstOrDefault(location => location.IsInSource);
        var sourcePath = sourceLocation?.SourceTree?.FilePath ?? string.Empty;
        var sourceStart = sourceLocation?.SourceSpan.Start.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
        return string.Join(
            "|",
            symbol.Kind.ToString(),
            symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            sourcePath,
            sourceStart);
    }

    private static string CreateCallGraphDescriptorIdentity(SymbolDescriptor descriptor)
    {
        if (descriptor.Key is not null)
        {
            return descriptor.Key.Value;
        }

        return string.Join(
            "|",
            descriptor.ProjectName,
            descriptor.Kind.ToString(),
            descriptor.ContainingNamespace,
            descriptor.ContainingType,
            descriptor.Name,
            descriptor.Span?.FilePath ?? string.Empty,
            descriptor.Span?.StartLine.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
            descriptor.Span?.StartColumn.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty);
    }

    private static string CreateCallGraphEdgeIdentity(CallGraphEdge edge)
    {
        return string.Join(
            "|",
            CreateCallGraphDescriptorIdentity(edge.Source),
            CreateCallGraphDescriptorIdentity(edge.Target),
            edge.Kind.ToString(),
            edge.Span.FilePath,
            edge.Span.StartLine.ToString(System.Globalization.CultureInfo.InvariantCulture),
            edge.Span.StartColumn.ToString(System.Globalization.CultureInfo.InvariantCulture),
            edge.Span.EndLine.ToString(System.Globalization.CultureInfo.InvariantCulture),
            edge.Span.EndColumn.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    private static string GetContainingTypeDisplay(ISymbol? containingSymbol)
    {
        return containingSymbol switch
        {
            INamedTypeSymbol namedType => namedType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
            _ when containingSymbol?.ContainingType is not null => containingSymbol.ContainingType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
            _ => string.Empty,
        };
    }

    private static bool IsInvocationExpressionTarget(SyntaxNode node)
    {
        return node.Parent is InvocationExpressionSyntax invocation
            && ReferenceEquals(invocation.Expression, node);
    }

    private static bool IsMemberAccessName(IdentifierNameSyntax identifier)
    {
        return identifier.Parent is MemberAccessExpressionSyntax memberAccess
            && ReferenceEquals(memberAccess.Name, identifier);
    }

    private static string[] ReadTargetFrameworks(string? projectFilePath, ICollection<string> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(projectFilePath) || !File.Exists(projectFilePath))
        {
            return Array.Empty<string>();
        }

        try
        {
            var document = XDocument.Load(projectFilePath);
            return document
                .Descendants()
                .Where(element => element.Name.LocalName is "TargetFramework" or "TargetFrameworks")
                .SelectMany(element => (element.Value ?? string.Empty)
                    .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                .Select(value => value.Trim())
                .Where(value => value.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (Exception ex) when (IsRecoverableException(ex))
        {
            diagnostics.Add($"WorkspaceStatusTargetFrameworkReadFailed: {projectFilePath}: {ex.Message}");
            return Array.Empty<string>();
        }
    }

    private static string ReadSolutionPlatformName(EnvDTE.SolutionConfiguration? configuration)
    {
        try
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var contexts = configuration?.SolutionContexts;
            if (contexts is null)
            {
                return string.Empty;
            }

            foreach (EnvDTE.SolutionContext context in contexts)
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                var platform = context.PlatformName ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(platform))
                {
                    return platform;
                }
            }
        }
        catch (Exception ex) when (IsRecoverableException(ex))
        {
            return string.Empty;
        }

        return string.Empty;
    }

    private static string ReadDynamicString(Func<string?> read)
    {
        try
        {
            return read() ?? string.Empty;
        }
        catch (Exception ex) when (IsRecoverableException(ex))
        {
            return string.Empty;
        }
    }

    private static object? ReadDynamicObject(Func<object?> read)
    {
        try
        {
            return read();
        }
        catch (Exception ex) when (IsRecoverableException(ex))
        {
            return null;
        }
    }

    private static int ReadDynamicInt(Func<int> read)
    {
        try
        {
            return read();
        }
        catch (Exception ex) when (IsRecoverableException(ex))
        {
            return 0;
        }
    }

    private static bool IsRecoverableException(Exception exception)
    {
        return exception is not OperationCanceledException;
    }

    private async Task<RequiredSolutionResult> GetRequiredSolutionAsync(CancellationToken cancellationToken)
    {
        var workspaceResult = await TryGetWorkspaceAsync(cancellationToken).ConfigureAwait(false);
        if (workspaceResult.Workspace is null)
        {
            return RequiredSolutionResult.FromFailure(
                workspaceResult.Diagnostic ?? "WorkspaceUnavailable: VisualStudioWorkspace is not available.");
        }

        var solution = workspaceResult.Workspace.CurrentSolution;
        if (string.IsNullOrWhiteSpace(solution.FilePath) && !solution.Projects.Any())
        {
            return RequiredSolutionResult.FromFailure("NoSolutionLoaded: no solution is loaded in this Visual Studio instance.");
        }

        return RequiredSolutionResult.Success(solution);
    }

    private async Task<WorkspaceLookupResult> TryGetWorkspaceAsync(CancellationToken cancellationToken)
    {
        if (_workspace is not null)
        {
            return WorkspaceLookupResult.Success(_workspace);
        }

        try
        {
            var componentModelResult = await TryGetComponentModelAsync(cancellationToken).ConfigureAwait(false);
            if (componentModelResult.ComponentModel is null)
            {
                return WorkspaceLookupResult.Failure(
                    componentModelResult.Diagnostic ?? "WorkspaceUnavailable: SComponentModel service is not available.");
            }

            _workspace = componentModelResult.ComponentModel.GetService<VisualStudioWorkspace>();
            return _workspace is null
                ? WorkspaceLookupResult.Failure("WorkspaceUnavailable: VisualStudioWorkspace MEF service is not available.")
                : WorkspaceLookupResult.Success(_workspace);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            BridgeLog.Error("Failed to obtain VisualStudioWorkspace.", ex);
            return WorkspaceLookupResult.Failure("WorkspaceUnavailable: failed to obtain VisualStudioWorkspace: " + ex.Message);
        }
    }

    private async Task<ComponentModelLookupResult> TryGetComponentModelAsync(CancellationToken cancellationToken)
    {
        if (_componentModel is not null)
        {
            return ComponentModelLookupResult.Success(_componentModel);
        }

        try
        {
            await _package.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
            _componentModel = await _package.GetServiceAsync(typeof(SComponentModel)).ConfigureAwait(true) as IComponentModel;
            return _componentModel is null
                ? ComponentModelLookupResult.Failure("WorkspaceUnavailable: SComponentModel service is not available.")
                : ComponentModelLookupResult.Success(_componentModel);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            BridgeLog.Error("Failed to obtain SComponentModel.", ex);
            return ComponentModelLookupResult.Failure("WorkspaceUnavailable: failed to obtain SComponentModel: " + ex.Message);
        }
    }

    private async Task<SymbolResolveResult> ResolveRequestedSymbolAsync(
        SymbolReferenceRequest request,
        CancellationToken cancellationToken)
    {
        var solutionResult = await GetRequiredSolutionAsync(cancellationToken).ConfigureAwait(false);
        if (solutionResult.Failure is not null)
        {
            return SymbolResolveResult.FromFailure(solutionResult.Failure);
        }

        var solution = solutionResult.Solution!;
        if (request.SymbolKey is not null)
        {
            var resolvedByKey = await ResolveSymbolKeyAsync(solution, request.SymbolKey.Value, cancellationToken)
                .ConfigureAwait(false);
            if (resolvedByKey is not null)
            {
                return SymbolResolveResult.Success(solution, resolvedByKey);
            }

            return SymbolResolveResult.FromFailure("RoslynQueryFailed: the supplied SymbolKey could not be resolved in the current solution.");
        }

        if (request.Position is null)
        {
            return SymbolResolveResult.FromFailure("MalformedRequest: provide either SymbolKey or source position.");
        }

        var symbol = await ResolveSymbolAtPositionAsync(solution, request.Position, request.IncludeGeneratedCode, cancellationToken)
            .ConfigureAwait(false);
        if (symbol is null)
        {
            return SymbolResolveResult.FromFailure("RoslynQueryFailed: no symbol was found at the supplied source position.");
        }

        return SymbolResolveResult.Success(solution, symbol);
    }

    private static async Task<ISymbol?> ResolveSymbolKeyAsync(
        Solution solution,
        string symbolKey,
        CancellationToken cancellationToken)
    {
        foreach (var project in solution.Projects.Where(project => project.Language == LanguageNames.CSharp))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var compilation = await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);
            if (compilation is null)
            {
                continue;
            }

            var declarationId = UnwrapSymbolKey(symbolKey);
            var resolvedSymbol = DocumentationCommentId.GetFirstSymbolForDeclarationId(
                declarationId,
                compilation);
            if (resolvedSymbol is not null)
            {
                return resolvedSymbol;
            }
        }

        return null;
    }

    private static async Task<ISymbol?> ResolveSymbolAtPositionAsync(
        Solution solution,
        SourceSpan position,
        bool includeGeneratedCode,
        CancellationToken cancellationToken)
    {
        var document = await FindDocumentByPathAsync(solution, position.FilePath, includeGeneratedCode, cancellationToken)
            .ConfigureAwait(false);
        if (document is null)
        {
            return null;
        }

        var sourceText = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
        var absolutePosition = GetAbsolutePosition(sourceText, position.StartLine, position.StartColumn);
        if (absolutePosition is null)
        {
            return null;
        }

        var syntaxRoot = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (syntaxRoot is null || semanticModel is null)
        {
            return null;
        }

        var token = syntaxRoot.FindToken(absolutePosition.Value);
        for (var node = token.Parent; node is not null; node = node.Parent)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var declaredSymbol = semanticModel.GetDeclaredSymbol(node, cancellationToken);
            if (declaredSymbol is not null)
            {
                return declaredSymbol;
            }

            var symbol = semanticModel.GetSymbolInfo(node, cancellationToken).Symbol;
            if (symbol is not null)
            {
                return symbol;
            }
        }

        return null;
    }

    private static int? GetAbsolutePosition(SourceText sourceText, int oneBasedLine, int oneBasedColumn)
    {
        if (oneBasedLine <= 0 || oneBasedColumn <= 0 || oneBasedLine > sourceText.Lines.Count)
        {
            return null;
        }

        var line = sourceText.Lines[oneBasedLine - 1];
        var zeroBasedColumn = oneBasedColumn - 1;
        if (zeroBasedColumn > line.Span.Length)
        {
            return null;
        }

        return line.Start + zeroBasedColumn;
    }

    private static async Task<Document?> FindDocumentByPathAsync(
        Solution solution,
        string filePath,
        bool includeSourceGeneratedDocuments,
        CancellationToken cancellationToken)
    {
        var document = FindDocumentByPath(solution, filePath);
        if (document is not null || !includeSourceGeneratedDocuments)
        {
            return document;
        }

        var normalized = NormalizePath(filePath);
        foreach (var project in solution.Projects.Where(project => project.Language == LanguageNames.CSharp))
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<Document> generatedDocuments;
            try
            {
                generatedDocuments = await GetSourceGeneratedDocumentsAsync(project, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                BridgeLog.Warning($"Source-generated documents could not be read for project '{project.Name}' while resolving '{filePath}': {ex.Message}");
                continue;
            }

            var generatedDocument = generatedDocuments.FirstOrDefault(item => string.Equals(
                NormalizePath(item.FilePath ?? string.Empty),
                normalized,
                StringComparison.OrdinalIgnoreCase));
            if (generatedDocument is not null)
            {
                return generatedDocument;
            }
        }

        return null;
    }

    private static Document? FindDocumentByPath(Solution solution, string filePath)
    {
        var normalized = NormalizePath(filePath);
        return solution.Projects
            .SelectMany(project => project.Documents)
            .FirstOrDefault(document => string.Equals(
                NormalizePath(document.FilePath ?? string.Empty),
                normalized,
                StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<Document?> FindDocumentBySyntaxTreeAsync(
        Solution solution,
        SyntaxTree syntaxTree,
        bool includeSourceGeneratedDocuments,
        CancellationToken cancellationToken)
    {
        var document = solution.GetDocument(syntaxTree);
        if (document is not null || !includeSourceGeneratedDocuments)
        {
            return document;
        }

        foreach (var project in solution.Projects.Where(project => project.Language == LanguageNames.CSharp))
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<Document> generatedDocuments;
            try
            {
                generatedDocuments = await GetSourceGeneratedDocumentsAsync(project, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                BridgeLog.Warning($"Source-generated documents could not be read for project '{project.Name}' while resolving a syntax tree: {ex.Message}");
                continue;
            }

            foreach (var generatedDocument in generatedDocuments)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (generatedDocument.TryGetSyntaxTree(out var generatedTree)
                    && ReferenceEquals(generatedTree, syntaxTree))
                {
                    return generatedDocument;
                }
            }
        }

        return null;
    }

    private static async Task<IReadOnlyList<Document>> EnumerateProjectDocumentsAsync(
        Project project,
        bool includeSourceGeneratedDocuments,
        List<string>? diagnostics,
        CancellationToken cancellationToken)
    {
        var documents = new List<Document>(project.Documents);
        if (!includeSourceGeneratedDocuments)
        {
            return documents;
        }

        try
        {
            documents.AddRange(await GetSourceGeneratedDocumentsAsync(project, cancellationToken).ConfigureAwait(false));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            diagnostics?.Add($"Source-generated documents could not be read for project '{project.Name}': {ex.Message}");
        }

        return documents;
    }

    private static async Task<IReadOnlyList<Document>> GetSourceGeneratedDocumentsAsync(
        Project project,
        CancellationToken cancellationToken)
    {
        var documents = await project.GetSourceGeneratedDocumentsAsync(cancellationToken).ConfigureAwait(false);
        return documents.Cast<Document>().ToArray();
    }

    private static IEnumerable<ISymbol> EnumerateDeclaredSymbols(
        SyntaxNode syntaxRoot,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        foreach (var node in syntaxRoot.DescendantNodes())
        {
            cancellationToken.ThrowIfCancellationRequested();
            switch (node)
            {
                case BaseTypeDeclarationSyntax:
                case DelegateDeclarationSyntax:
                case MethodDeclarationSyntax:
                case ConstructorDeclarationSyntax:
                case DestructorDeclarationSyntax:
                case OperatorDeclarationSyntax:
                case ConversionOperatorDeclarationSyntax:
                case PropertyDeclarationSyntax:
                case EventDeclarationSyntax:
                case NamespaceDeclarationSyntax:
                case FileScopedNamespaceDeclarationSyntax:
                    var symbol = semanticModel.GetDeclaredSymbol(node, cancellationToken);
                    if (symbol is not null)
                    {
                        yield return symbol;
                    }

                    break;
                case FieldDeclarationSyntax fieldDeclaration:
                    foreach (var variable in fieldDeclaration.Declaration.Variables)
                    {
                        var fieldSymbol = semanticModel.GetDeclaredSymbol(variable, cancellationToken);
                        if (fieldSymbol is not null)
                        {
                            yield return fieldSymbol;
                        }
                    }

                    break;
                case EventFieldDeclarationSyntax eventFieldDeclaration:
                    foreach (var variable in eventFieldDeclaration.Declaration.Variables)
                    {
                        var eventSymbol = semanticModel.GetDeclaredSymbol(variable, cancellationToken);
                        if (eventSymbol is not null)
                        {
                            yield return eventSymbol;
                        }
                    }

                    break;
            }
        }
    }

    private static bool MatchesQuery(ISymbol symbol, string queryText)
    {
        return symbol.Name.IndexOf(queryText, StringComparison.OrdinalIgnoreCase) >= 0
            || symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)
                .IndexOf(queryText, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static IEnumerable<SymbolDescriptor> OrderSearchResults(
        IEnumerable<SymbolDescriptor> results,
        string queryText)
    {
        return results
            .OrderByDescending(result => string.Equals(result.Name, queryText, StringComparison.OrdinalIgnoreCase))
            .ThenBy(result => result.Name.IndexOf(queryText, StringComparison.OrdinalIgnoreCase) < 0 ? 1 : 0)
            .ThenBy(result => result.ProjectName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(result => result.Span?.FilePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(result => result.Span?.StartLine ?? int.MaxValue);
    }

    private static string CreateSymbolDescriptorIdentity(SymbolDescriptor descriptor)
    {
        var span = descriptor.Span;
        return string.Join(
            "|",
            descriptor.ProjectName,
            descriptor.Key?.Value ?? string.Empty,
            descriptor.Kind.ToString(),
            descriptor.ContainingNamespace,
            descriptor.ContainingType,
            descriptor.Name,
            NormalizePath(span?.FilePath ?? string.Empty),
            span?.StartLine.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
            span?.StartColumn.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
            span?.EndLine.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
            span?.EndColumn.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty);
    }

    private static SymbolDescriptor? CreateDescriptor(
        ISymbol symbol,
        Solution solution,
        CancellationToken cancellationToken)
    {
        var project = FindProjectForSymbol(solution, symbol, cancellationToken);
        return project is null ? null : CreateDescriptor(symbol, project, cancellationToken);
    }

    private static SymbolReference[] CreateRoleReferences(
        IEnumerable<ISymbol> symbols,
        Solution solution,
        bool includeGeneratedCode,
        ReferenceRole role,
        int maxResults,
        CancellationToken cancellationToken,
        out bool isTruncated)
    {
        var items = new List<SymbolReference>();
        isTruncated = false;
        foreach (var symbol in symbols)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var descriptor = CreateDescriptor(symbol, solution, cancellationToken);
            if (descriptor?.Span is null)
            {
                continue;
            }

            if (!includeGeneratedCode && IsGeneratedPath(descriptor.Span.FilePath))
            {
                continue;
            }

            items.Add(new SymbolReference
            {
                Symbol = descriptor,
                Span = descriptor.Span,
                Role = role,
            });

            if (items.Count >= maxResults)
            {
                isTruncated = true;
                break;
            }
        }

        return items.ToArray();
    }

    private static SymbolDescriptor? CreateDescriptor(
        ISymbol symbol,
        Project project,
        CancellationToken cancellationToken)
    {
        var sourceLocation = symbol.Locations.FirstOrDefault(location => location.IsInSource);
        var span = sourceLocation is null ? null : CreateSpan(sourceLocation);

        return new SymbolDescriptor
        {
            Key = CreateSymbolKey(symbol),
            Name = symbol.Name,
            ContainingType = symbol.ContainingType?.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat) ?? string.Empty,
            ContainingNamespace = symbol.ContainingNamespace is null || symbol.ContainingNamespace.IsGlobalNamespace
                ? string.Empty
                : symbol.ContainingNamespace.ToDisplayString(),
            ProjectName = project.Name,
            Kind = MapKind(symbol),
            Span = span,
        };
    }

    private static SymbolDescriptor CreateDescriptorIncludingMetadata(
        ISymbol symbol,
        Solution solution,
        CancellationToken cancellationToken)
    {
        var project = FindProjectForSymbol(solution, symbol, cancellationToken);
        var sourceLocation = symbol.Locations.FirstOrDefault(location => location.IsInSource);
        var span = sourceLocation is null ? null : CreateSpan(sourceLocation);

        return new SymbolDescriptor
        {
            Key = CreateSymbolKey(symbol),
            Name = symbol.Name,
            ContainingType = symbol.ContainingType?.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat) ?? string.Empty,
            ContainingNamespace = symbol.ContainingNamespace is null || symbol.ContainingNamespace.IsGlobalNamespace
                ? string.Empty
                : symbol.ContainingNamespace.ToDisplayString(),
            ProjectName = project?.Name ?? symbol.ContainingAssembly?.Name ?? string.Empty,
            Kind = MapKind(symbol),
            Span = span,
        };
    }

    private static string? FindTemporaryMarker(string text)
    {
        var markers = new[]
        {
            "TODO",
            "FIXME",
            "HACK",
            "TEMP",
            "WORKAROUND",
            "临时",
            "簡化",
            "简化",
            "占位",
        };

        return markers.FirstOrDefault(marker =>
            text.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0);
    }

    private static string? FindTemporaryMarker(SyntaxNode node)
    {
        if (node is ThrowStatementSyntax { Expression: ObjectCreationExpressionSyntax creation })
        {
            var typeName = creation.Type.ToString();
            if (typeName.EndsWith("NotImplementedException", StringComparison.Ordinal)
                || typeName.EndsWith("NotImplementedException()", StringComparison.Ordinal))
            {
                return "NotImplementedException";
            }

            if (typeName.EndsWith("NotSupportedException", StringComparison.Ordinal)
                || typeName.EndsWith("NotSupportedException()", StringComparison.Ordinal))
            {
                return "NotSupportedException";
            }
        }

        if (node is CatchClauseSyntax { Block.Statements.Count: 0 })
        {
            return "EmptyCatch";
        }

        return null;
    }

    private static string Truncate(string value, int maxLength)
    {
        return value.Length <= maxLength ? value : value.Substring(0, maxLength);
    }

    private static ISymbol? FindContainingDeclaredSymbol(
        SyntaxNode? node,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        for (var current = node; current is not null; current = current.Parent)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var symbol = semanticModel.GetDeclaredSymbol(current, cancellationToken);
            if (symbol is not null)
            {
                return symbol;
            }
        }

        return null;
    }

    private static string? FindNearestSymbolDisplay<TSyntax>(
        SyntaxNode? node,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
        where TSyntax : SyntaxNode
    {
        for (var current = node; current is not null; current = current.Parent)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (current is not TSyntax)
            {
                continue;
            }

            var symbol = semanticModel.GetDeclaredSymbol(current, cancellationToken);
            if (symbol is not null)
            {
                return symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
            }
        }

        return null;
    }

    private static string FindNearestMemberDisplay(
        SyntaxNode? node,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        for (var current = node; current is not null; current = current.Parent)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (current is not BaseMethodDeclarationSyntax
                && current is not PropertyDeclarationSyntax
                && current is not EventDeclarationSyntax
                && current is not FieldDeclarationSyntax
                && current is not AccessorDeclarationSyntax)
            {
                continue;
            }

            var symbol = semanticModel.GetDeclaredSymbol(current, cancellationToken);
            if (symbol is not null)
            {
                return symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
            }
        }

        return string.Empty;
    }

    private static string FindNearestLambdaDisplay(SyntaxNode? node)
    {
        for (var current = node; current is not null; current = current.Parent)
        {
            if (current is SimpleLambdaExpressionSyntax
                or ParenthesizedLambdaExpressionSyntax
                or AnonymousMethodExpressionSyntax)
            {
                return Truncate(current.ToString(), 160);
            }
        }

        return string.Empty;
    }

    private static IReadOnlyList<SymbolDescriptor> CreateEnclosingAncestorDescriptors(
        SyntaxNode? node,
        SemanticModel semanticModel,
        Project project,
        CancellationToken cancellationToken)
    {
        var descriptors = new List<SymbolDescriptor>();
        var seen = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        for (var current = node; current is not null; current = current.Parent)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var symbol = semanticModel.GetDeclaredSymbol(current, cancellationToken);
            if (symbol is null || !seen.Add(symbol))
            {
                continue;
            }

            var descriptor = CreateDescriptor(symbol, project, cancellationToken);
            if (descriptor is not null)
            {
                descriptors.Add(descriptor);
            }
        }

        descriptors.Reverse();
        return descriptors;
    }

    private static Project? FindProjectForSymbol(
        Solution solution,
        ISymbol symbol,
        CancellationToken cancellationToken)
    {
        var sourceTree = symbol.Locations.FirstOrDefault(location => location.IsInSource)?.SourceTree;
        if (sourceTree is null)
        {
            return solution.GetProject(symbol.ContainingAssembly);
        }

        var document = solution.GetDocument(sourceTree);
        if (document is not null)
        {
            return document.Project;
        }

        foreach (var project in solution.Projects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (project.Documents.Any(document => document.TryGetSyntaxTree(out var tree) && ReferenceEquals(tree, sourceTree)))
            {
                return project;
            }
        }

        return solution.GetProject(symbol.ContainingAssembly);
    }

    private static SourceSpan? CreateSpan(Location location)
    {
        if (!location.IsInSource || location.SourceTree?.FilePath is null)
        {
            return null;
        }

        var lineSpan = location.GetLineSpan();
        return new SourceSpan
        {
            FilePath = location.SourceTree.FilePath,
            StartLine = lineSpan.StartLinePosition.Line + 1,
            StartColumn = lineSpan.StartLinePosition.Character + 1,
            EndLine = lineSpan.EndLinePosition.Line + 1,
            EndColumn = lineSpan.EndLinePosition.Character + 1,
        };
    }

    private static CodeSymbolKind MapKind(ISymbol symbol)
    {
        return symbol.Kind switch
        {
            SymbolKind.Namespace => CodeSymbolKind.Namespace,
            SymbolKind.NamedType => CodeSymbolKind.Type,
            SymbolKind.Method => CodeSymbolKind.Method,
            SymbolKind.Property => CodeSymbolKind.Property,
            SymbolKind.Field => CodeSymbolKind.Field,
            SymbolKind.Event => CodeSymbolKind.Event,
            SymbolKind.Parameter => CodeSymbolKind.Parameter,
            SymbolKind.Local => CodeSymbolKind.Local,
            _ => CodeSymbolKind.Unknown,
        };
    }

    private static Protocol.SymbolKey? CreateSymbolKey(ISymbol symbol)
    {
        var documentationCommentId = symbol.GetDocumentationCommentId();
        return string.IsNullOrWhiteSpace(documentationCommentId)
            ? null
            : new Protocol.SymbolKey("docid:" + documentationCommentId);
    }

    private static string UnwrapSymbolKey(string symbolKey)
    {
        const string prefix = "docid:";
        return symbolKey.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? symbolKey.Substring(prefix.Length)
            : symbolKey;
    }

    private static IEnumerable<ISymbol> ExpandDefinitionSymbols(ISymbol symbol)
    {
        var reduced = symbol.OriginalDefinition;
        yield return reduced;

        foreach (var location in symbol.Locations.Where(location => location.IsInSource))
        {
            if (!SymbolEqualityComparer.Default.Equals(reduced, symbol))
            {
                yield return symbol;
                yield break;
            }
        }
    }

    private static async Task<ReferenceRole> ClassifyReferenceRoleAsync(
        Document document,
        TextSpan sourceSpan,
        CancellationToken cancellationToken)
    {
        var syntaxRoot = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        return ReferenceRoleClassifier.Classify(syntaxRoot, sourceSpan);
    }

    private static bool IsGeneratedDocument(Document document)
    {
        return IsGeneratedPath(document.FilePath ?? document.Name);
    }

    private static bool IsGeneratedPath(string path)
    {
        var normalized = path.Replace('/', '\\');
        var fileName = Path.GetFileName(normalized);
        return normalized.IndexOf("\\obj\\", StringComparison.OrdinalIgnoreCase) >= 0
            || fileName.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".g.i.cs", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".designer.cs", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".generated.cs", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        try
        {
            return Path.GetFullPath(path.Trim());
        }
        catch (ArgumentException)
        {
            return path.Trim();
        }
        catch (NotSupportedException)
        {
            return path.Trim();
        }
    }

    private sealed class CallGraphTraversalItem
    {
        public CallGraphTraversalItem(ISymbol symbol, SymbolDescriptor descriptor)
        {
            Symbol = symbol;
            Descriptor = descriptor;
        }

        public ISymbol Symbol { get; }

        public SymbolDescriptor Descriptor { get; }
    }

    private sealed class CallGraphTraversalEdge
    {
        public CallGraphTraversalEdge(
            ISymbol nextSymbol,
            SymbolDescriptor nextDescriptor,
            CallGraphEdge edge)
        {
            NextSymbol = nextSymbol;
            NextDescriptor = nextDescriptor;
            Edge = edge;
        }

        public ISymbol NextSymbol { get; }

        public SymbolDescriptor NextDescriptor { get; }

        public CallGraphEdge Edge { get; }
    }

    private sealed class RelatedTestsTargetResult
    {
        private RelatedTestsTargetResult(RelatedTestsTargetContext? target, QueryFailure? failure)
        {
            Target = target;
            Failure = failure;
        }

        public RelatedTestsTargetContext? Target { get; }

        public QueryFailure? Failure { get; }

        public static RelatedTestsTargetResult Success(RelatedTestsTargetContext target) => new(target, null);

        public static RelatedTestsTargetResult FromFailure(string diagnostic) => new(null, new QueryFailure(diagnostic));
    }

    private sealed class RelatedTestsTargetContext
    {
        public RelatedTestsTargetContext(
            ISymbol? symbol,
            Document? document,
            string projectName,
            string @namespace,
            IReadOnlyList<string> needles)
        {
            Symbol = symbol;
            Document = document;
            ProjectName = projectName;
            Namespace = @namespace;
            Needles = needles;
        }

        public ISymbol? Symbol { get; }

        public Document? Document { get; }

        public string ProjectName { get; }

        public string Namespace { get; }

        public IReadOnlyList<string> Needles { get; }
    }

    private sealed class RelatedTestSymbolInfo
    {
        public RelatedTestSymbolInfo(
            ISymbol symbol,
            string testClass,
            string testMethod,
            string @namespace,
            bool hasKnownTestMethodAttribute,
            bool hasKnownTestClassAttribute)
        {
            Symbol = symbol;
            TestClass = testClass;
            TestMethod = testMethod;
            Namespace = @namespace;
            HasKnownTestMethodAttribute = hasKnownTestMethodAttribute;
            HasKnownTestClassAttribute = hasKnownTestClassAttribute;
        }

        public ISymbol Symbol { get; }

        public string TestClass { get; }

        public string TestMethod { get; }

        public string Namespace { get; }

        public bool HasKnownTestMethodAttribute { get; }

        public bool HasKnownTestClassAttribute { get; }
    }

    private sealed class RelatedTestAccumulator
    {
        private readonly int _maxResults;
        private readonly Dictionary<string, RelatedTestEntry> _entries = new(StringComparer.Ordinal);

        public RelatedTestAccumulator(int maxResults)
        {
            _maxResults = maxResults;
        }

        public bool IsTruncated { get; private set; }

        public bool HasReferenceMatch => _entries.Values.Any(entry => entry.Reasons.Contains("ReferenceMatch"));

        public void Add(RelatedTestDescriptor descriptor)
        {
            var key = CreateKey(descriptor);
            if (_entries.TryGetValue(key, out var entry))
            {
                entry.Merge(descriptor);
                return;
            }

            if (_entries.Count >= _maxResults)
            {
                IsTruncated = true;
                return;
            }

            _entries.Add(key, new RelatedTestEntry(descriptor));
        }

        public IReadOnlyList<RelatedTestDescriptor> ToDescriptors()
        {
            return _entries.Values
                .Select(entry => entry.ToDescriptor())
                .OrderBy(descriptor => descriptor.MatchReasons.Contains("ReferenceMatch") ? 0 : 1)
                .ThenBy(descriptor => descriptor.ProjectName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(descriptor => descriptor.TestClass, StringComparer.OrdinalIgnoreCase)
                .ThenBy(descriptor => descriptor.TestMethod, StringComparer.OrdinalIgnoreCase)
                .ThenBy(descriptor => descriptor.Span.FilePath, StringComparer.OrdinalIgnoreCase)
                .ThenBy(descriptor => descriptor.Span.StartLine)
                .ToArray();
        }

        private static string CreateKey(RelatedTestDescriptor descriptor)
        {
            if (descriptor.TestSymbol.Key is not null)
            {
                return descriptor.TestSymbol.Key.Value;
            }

            return string.Join(
                "|",
                descriptor.ProjectName,
                descriptor.Span.FilePath,
                descriptor.Span.StartLine.ToString(System.Globalization.CultureInfo.InvariantCulture),
                descriptor.Span.StartColumn.ToString(System.Globalization.CultureInfo.InvariantCulture),
                descriptor.TestClass,
                descriptor.TestMethod);
        }
    }

    private sealed class RelatedTestEntry
    {
        private readonly RelatedTestDescriptor _descriptor;
        private readonly HashSet<string> _reasons = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<SourceSpan> _evidenceSpans = new();

        public RelatedTestEntry(RelatedTestDescriptor descriptor)
        {
            _descriptor = descriptor;
            Merge(descriptor);
        }

        public ISet<string> Reasons => _reasons;

        public void Merge(RelatedTestDescriptor descriptor)
        {
            foreach (var reason in descriptor.MatchReasons)
            {
                if (!string.IsNullOrWhiteSpace(reason))
                {
                    _reasons.Add(reason);
                }
            }

            foreach (var evidenceSpan in descriptor.EvidenceSpans)
            {
                if (!_evidenceSpans.Any(existing => SameSpan(existing, evidenceSpan)))
                {
                    _evidenceSpans.Add(evidenceSpan);
                }
            }
        }

        public RelatedTestDescriptor ToDescriptor()
        {
            return new RelatedTestDescriptor
            {
                ProjectName = _descriptor.ProjectName,
                TestFramework = _descriptor.TestFramework,
                TestClass = _descriptor.TestClass,
                TestMethod = _descriptor.TestMethod,
                TestSymbol = _descriptor.TestSymbol,
                Span = _descriptor.Span,
                MatchReasons = _reasons.OrderBy(reason => reason, StringComparer.OrdinalIgnoreCase).ToArray(),
                EvidenceSpans = _evidenceSpans
                    .OrderBy(span => span.FilePath, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(span => span.StartLine)
                    .ThenBy(span => span.StartColumn)
                    .ToArray(),
            };
        }

        private static bool SameSpan(SourceSpan left, SourceSpan right)
        {
            return string.Equals(left.FilePath, right.FilePath, StringComparison.OrdinalIgnoreCase)
                && left.StartLine == right.StartLine
                && left.StartColumn == right.StartColumn
                && left.EndLine == right.EndLine
                && left.EndColumn == right.EndColumn;
        }
    }

    private sealed class WorkspaceLookupResult
    {
        private WorkspaceLookupResult(VisualStudioWorkspace? workspace, string? diagnostic)
        {
            Workspace = workspace;
            Diagnostic = diagnostic;
        }

        public VisualStudioWorkspace? Workspace { get; }

        public string? Diagnostic { get; }

        public static WorkspaceLookupResult Success(VisualStudioWorkspace workspace) => new(workspace, null);

        public static WorkspaceLookupResult Failure(string diagnostic) => new(null, diagnostic);
    }

    private sealed class ComponentModelLookupResult
    {
        private ComponentModelLookupResult(IComponentModel? componentModel, string? diagnostic)
        {
            ComponentModel = componentModel;
            Diagnostic = diagnostic;
        }

        public IComponentModel? ComponentModel { get; }

        public string? Diagnostic { get; }

        public static ComponentModelLookupResult Success(IComponentModel componentModel) => new(componentModel, null);

        public static ComponentModelLookupResult Failure(string diagnostic) => new(null, diagnostic);
    }

    private sealed class RequiredSolutionResult
    {
        private RequiredSolutionResult(Solution? solution, QueryFailure? failure)
        {
            Solution = solution;
            Failure = failure;
        }

        public Solution? Solution { get; }

        public QueryFailure? Failure { get; }

        public static RequiredSolutionResult Success(Solution solution) => new(solution, null);

        public static RequiredSolutionResult FromFailure(string diagnostic) => new(null, new QueryFailure(diagnostic));
    }

    private sealed class SymbolResolveResult
    {
        private SymbolResolveResult(Solution? solution, ISymbol? symbol, QueryFailure? failure)
        {
            Solution = solution;
            Symbol = symbol;
            Failure = failure;
        }

        public Solution? Solution { get; }

        public ISymbol? Symbol { get; }

        public QueryFailure? Failure { get; }

        public static SymbolResolveResult Success(Solution solution, ISymbol symbol) => new(solution, symbol, null);

        public static SymbolResolveResult FromFailure(QueryFailure failure) => new(null, null, failure);

        public static SymbolResolveResult FromFailure(string diagnostic) => new(null, null, new QueryFailure(diagnostic));
    }

    private sealed class SymbolImpactSiteCollectionResult
    {
        private SymbolImpactSiteCollectionResult(
            IReadOnlyList<SymbolImpactSite>? sites,
            IReadOnlyList<string>? diagnostics,
            bool isPartial,
            QueryFailure? failure)
        {
            Sites = sites;
            Diagnostics = diagnostics;
            IsPartial = isPartial;
            Failure = failure;
        }

        public IReadOnlyList<SymbolImpactSite>? Sites { get; }

        public IReadOnlyList<string>? Diagnostics { get; }

        public bool IsPartial { get; }

        public QueryFailure? Failure { get; }

        public static SymbolImpactSiteCollectionResult Success(
            IReadOnlyList<SymbolImpactSite> sites,
            IReadOnlyList<string> diagnostics,
            bool isPartial)
        {
            return new SymbolImpactSiteCollectionResult(sites, diagnostics, isPartial, null);
        }

        public static SymbolImpactSiteCollectionResult FromFailure(string diagnostic)
        {
            return new SymbolImpactSiteCollectionResult(null, null, false, new QueryFailure(diagnostic));
        }
    }

    private sealed class QueryFailure
    {
        public QueryFailure(string diagnostic)
        {
            Diagnostic = diagnostic;
        }

        public string Diagnostic { get; }

        public WorkspaceQueryResult<T> As<T>() => Failure<T>(Diagnostic);
    }
}
