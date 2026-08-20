using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using VisualStudio.CSharpNavigator.Protocol;

namespace VisualStudio.CSharpNavigator.Vsix.Workspace;

internal sealed partial class VisualStudioWorkspaceQueryService
{
    public async Task<WorkspaceQueryResult<SymbolDescriptor>> SearchSymbolsAsync(
        SymbolSearchRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.QueryText))
        {
            return Failure<SymbolDescriptor>("QueryText is required.");
        }

        if (request.MaxResults is < 1 or > 500)
        {
            return Failure<SymbolDescriptor>("MaxResults must be between 1 and 500.");
        }

        var solutionResult = await GetRequiredSolutionAsync(cancellationToken).ConfigureAwait(false);
        if (solutionResult.Failure is not null)
        {
            return solutionResult.Failure.As<SymbolDescriptor>();
        }

        var solution = solutionResult.Solution!;
        var diagnostics = new List<string>();
        var isPartial = false;
        if (request.IncludeGeneratedCode)
        {
            diagnostics.Add("IncludeGeneratedCode=true: results include generated documents that are visible in the current VisualStudioWorkspace.");
        }

        var results = new List<SymbolDescriptor>();
        var seenResults = new HashSet<string>(StringComparer.Ordinal);
        foreach (var project in solution.Projects.Where(project => project.Language == LanguageNames.CSharp))
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var document in await EnumerateProjectDocumentsAsync(project, request.IncludeGeneratedCode, diagnostics, cancellationToken)
                         .ConfigureAwait(false))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!request.IncludeGeneratedCode && IsGeneratedDocument(document))
                {
                    continue;
                }

                var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
                var syntaxRoot = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
                if (semanticModel is null || syntaxRoot is null)
                {
                    diagnostics.Add($"Document '{document.FilePath ?? document.Name}' has no semantic model or syntax root.");
                    isPartial = true;
                    continue;
                }

                foreach (var symbol in EnumerateDeclaredSymbols(syntaxRoot, semanticModel, cancellationToken))
                {
                    if (!MatchesQuery(symbol, request.QueryText))
                    {
                        continue;
                    }

                    var descriptor = CreateDescriptor(symbol, project, cancellationToken);
                    if (descriptor?.Span is null)
                    {
                        continue;
                    }

                    if (!seenResults.Add(CreateSymbolDescriptorIdentity(descriptor)))
                    {
                        continue;
                    }

                    results.Add(descriptor);
                }
            }
        }

        return Success(
            OrderSearchResults(results, request.QueryText).Take(request.MaxResults).ToArray(),
            diagnostics,
            isPartial);
    }

    public async Task<WorkspaceQueryResult<SymbolReference>> FindReferencesAsync(
        SymbolReferenceRequest request,
        CancellationToken cancellationToken)
    {
        if (request.MaxResults is < 1 or > 10000)
        {
            return Failure<SymbolReference>("MaxResults must be between 1 and 10000.");
        }

        var symbolResult = await ResolveRequestedSymbolAsync(request, cancellationToken).ConfigureAwait(false);
        if (symbolResult.Failure is not null)
        {
            return symbolResult.Failure.As<SymbolReference>();
        }

        var symbol = symbolResult.Symbol!;
        var solution = symbolResult.Solution!;
        var references = await SymbolFinder.FindReferencesAsync(symbol, solution, cancellationToken)
            .ConfigureAwait(false);

        var items = new List<SymbolReference>();
        var diagnostics = new List<string>();
        foreach (var referencedSymbol in references)
        {
            var descriptor = CreateDescriptor(referencedSymbol.Definition, solution, cancellationToken);
            if (descriptor is null)
            {
                continue;
            }

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

                items.Add(new SymbolReference
                {
                    Symbol = descriptor,
                    Span = span,
                    Role = role,
                });

                if (items.Count >= request.MaxResults)
                {
                    diagnostics.Add($"References were truncated at MaxResults={request.MaxResults}.");
                    return Success(items.ToArray(), diagnostics, isPartial: true);
                }
            }
        }

        return Success(items.ToArray(), diagnostics, isPartial: false);
    }

    public async Task<WorkspaceQueryResult<SymbolDescriptor>> FindDefinitionsAsync(
        SymbolReferenceRequest request,
        CancellationToken cancellationToken)
    {
        var symbolResult = await ResolveRequestedSymbolAsync(request, cancellationToken).ConfigureAwait(false);
        if (symbolResult.Failure is not null)
        {
            return symbolResult.Failure.As<SymbolDescriptor>();
        }

        var definitions = new List<SymbolDescriptor>();
        foreach (var candidate in ExpandDefinitionSymbols(symbolResult.Symbol!))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var descriptor = CreateDescriptor(candidate, symbolResult.Solution!, cancellationToken);
            if (descriptor?.Span is null)
            {
                continue;
            }

            if (!request.IncludeGeneratedCode && IsGeneratedPath(descriptor.Span.FilePath))
            {
                continue;
            }

            definitions.Add(descriptor);
        }

        if (definitions.Count == 0)
        {
            return Failure<SymbolDescriptor>("No source definitions were found for the requested symbol.");
        }

        return Success(definitions.ToArray());
    }

    public async Task<WorkspaceQueryResult<SymbolReference>> FindImplementationsAsync(
        SymbolReferenceRequest request,
        CancellationToken cancellationToken)
    {
        if (request.MaxResults is < 1 or > 10000)
        {
            return Failure<SymbolReference>("MaxResults must be between 1 and 10000.");
        }

        var symbolResult = await ResolveRequestedSymbolAsync(request, cancellationToken).ConfigureAwait(false);
        if (symbolResult.Failure is not null)
        {
            return symbolResult.Failure.As<SymbolReference>();
        }

        var implementations = await SymbolFinder.FindImplementationsAsync(
                symbolResult.Symbol!,
                symbolResult.Solution!,
                projects: null,
                cancellationToken)
            .ConfigureAwait(false);

        var items = CreateRoleReferences(
            implementations,
            symbolResult.Solution!,
            request.IncludeGeneratedCode,
            ReferenceRole.Implementation,
            request.MaxResults,
            cancellationToken,
            out var isTruncated);
        var diagnostics = isTruncated
            ? new[] { $"Implementations were truncated at MaxResults={request.MaxResults}." }
            : Array.Empty<string>();
        return Success(items, diagnostics, isTruncated);
    }

    public async Task<WorkspaceQueryResult<SymbolReference>> FindOverridesAsync(
        SymbolReferenceRequest request,
        CancellationToken cancellationToken)
    {
        if (request.MaxResults is < 1 or > 10000)
        {
            return Failure<SymbolReference>("MaxResults must be between 1 and 10000.");
        }

        var symbolResult = await ResolveRequestedSymbolAsync(request, cancellationToken).ConfigureAwait(false);
        if (symbolResult.Failure is not null)
        {
            return symbolResult.Failure.As<SymbolReference>();
        }

        var overrides = await SymbolFinder.FindOverridesAsync(
                symbolResult.Symbol!,
                symbolResult.Solution!,
                projects: null,
                cancellationToken)
            .ConfigureAwait(false);

        var items = CreateRoleReferences(
            overrides,
            symbolResult.Solution!,
            request.IncludeGeneratedCode,
            ReferenceRole.Override,
            request.MaxResults,
            cancellationToken,
            out var isTruncated);
        var diagnostics = isTruncated
            ? new[] { $"Overrides were truncated at MaxResults={request.MaxResults}." }
            : Array.Empty<string>();
        return Success(items, diagnostics, isTruncated);
    }

    public async Task<WorkspaceQueryResult<SymbolDescription>> DescribeSymbolAsync(
        SymbolDescriptionRequest request,
        CancellationToken cancellationToken)
    {
        var symbolResult = await ResolveRequestedSymbolAsync(ToSymbolReferenceRequest(request), cancellationToken)
            .ConfigureAwait(false);
        if (symbolResult.Failure is not null)
        {
            return symbolResult.Failure.As<SymbolDescription>();
        }

        var descriptor = CreateDescriptor(symbolResult.Symbol!, symbolResult.Solution!, cancellationToken);
        if (descriptor?.Span is null)
        {
            return Failure<SymbolDescription>("No source descriptor was found for the requested symbol.");
        }

        if (!request.IncludeGeneratedCode && IsGeneratedPath(descriptor.Span.FilePath))
        {
            return Failure<SymbolDescription>("The requested symbol is in generated code. Retry with IncludeGeneratedCode=true.");
        }

        return Success(new[] { CreateSymbolDescription(symbolResult.Symbol!, descriptor, cancellationToken) });
    }

}
