using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using VisualStudio.CSharpNavigator.Protocol;
using VisualStudio.CSharpNavigator.Roslyn;

namespace VisualStudio.CSharpNavigator.Vsix.Workspace;

internal sealed partial class VisualStudioWorkspaceQueryService
{
    public async Task<WorkspaceQueryResult<SourceContextSnippet>> GetSymbolSourceAsync(
        SourceContextRequest request,
        CancellationToken cancellationToken)
    {
        var validation = ValidateSourceContextRequest(request, requireSymbol: true);
        if (validation is not null)
        {
            return Failure<SourceContextSnippet>(validation);
        }

        var symbolResult = await ResolveRequestedSymbolAsync(ToSymbolReferenceRequest(request), cancellationToken)
            .ConfigureAwait(false);
        if (symbolResult.Failure is not null)
        {
            return symbolResult.Failure.As<SourceContextSnippet>();
        }

        var symbol = symbolResult.Symbol!;
        var solution = symbolResult.Solution!;
        var diagnostics = new List<string>();
        var snippets = new List<SourceContextSnippet>();
        var syntaxReferences = symbol.DeclaringSyntaxReferences;
        if (syntaxReferences.Length == 0)
        {
            return Failure<SourceContextSnippet>("SymbolNotInSource: the requested symbol has no source declaration.");
        }

        foreach (var syntaxReference in syntaxReferences.Take(request.MaxSnippets))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var node = await syntaxReference.GetSyntaxAsync(cancellationToken).ConfigureAwait(false);
            var document = await FindDocumentBySyntaxTreeAsync(
                    solution,
                    syntaxReference.SyntaxTree,
                    request.IncludeGeneratedCode,
                    cancellationToken)
                .ConfigureAwait(false);
            if (document is null)
            {
                diagnostics.Add("DocumentNotFound: a symbol declaration syntax tree was not found in the active solution.");
                continue;
            }

            if (!request.IncludeGeneratedCode && IsGeneratedDocument(document))
            {
                diagnostics.Add("A symbol declaration is in generated code and was omitted. Retry with IncludeGeneratedCode=true.");
                continue;
            }

            var sourceText = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
            var descriptor = CreateDescriptor(symbol, document.Project, cancellationToken);
            snippets.Add(CreateSourceContextSnippet(
                document,
                sourceText,
                node.Span,
                node.Span,
                descriptor,
                "symbol declaration",
                request.ContextLines,
                request.MaxChars,
                new[] { "SymbolDeclaration", symbol.Kind.ToString() }));
        }

        if (syntaxReferences.Length > request.MaxSnippets)
        {
            diagnostics.Add($"Symbol declarations were truncated at MaxSnippets={request.MaxSnippets}.");
        }

        if (snippets.Count == 0)
        {
            return Failure<SourceContextSnippet>(
                diagnostics.Count == 0
                    ? "No source snippets were found for the requested symbol."
                    : string.Join(" ", diagnostics));
        }

        return Success(snippets.ToArray(), diagnostics, syntaxReferences.Length > request.MaxSnippets || snippets.Any(item => item.IsTextTruncated));
    }

    public async Task<WorkspaceQueryResult<SourceContextSnippet>> GetSourceContextAsync(
        SourceContextRequest request,
        CancellationToken cancellationToken)
    {
        var validation = ValidateSourceContextRequest(request, requireSymbol: false);
        if (validation is not null)
        {
            return Failure<SourceContextSnippet>(validation);
        }

        var solutionResult = await GetRequiredSolutionAsync(cancellationToken).ConfigureAwait(false);
        if (solutionResult.Failure is not null)
        {
            return solutionResult.Failure.As<SourceContextSnippet>();
        }

        return await GetSourceContextFromSolutionAsync(request, solutionResult.Solution!, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<WorkspaceQueryResult<SourceContextSnippet>> GetSourceContextFromSolutionAsync(
        SourceContextRequest request,
        Solution solution,
        CancellationToken cancellationToken)
    {
        var position = request.Position!;
        var document = await FindDocumentByPathAsync(
                solution,
                position.FilePath,
                request.IncludeGeneratedCode,
                cancellationToken)
            .ConfigureAwait(false);
        if (document is null)
        {
            return Failure<SourceContextSnippet>("DocumentNotFound: the requested file is not in the active solution.");
        }

        if (!request.IncludeGeneratedCode && IsGeneratedDocument(document))
        {
            return Failure<SourceContextSnippet>("The requested document is generated code. Retry with IncludeGeneratedCode=true.");
        }

        var sourceText = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
        var absolutePosition = GetAbsolutePosition(sourceText, position.StartLine, position.StartColumn);
        if (absolutePosition is null)
        {
            return Failure<SourceContextSnippet>("The supplied line and column are outside the document.");
        }

        var syntaxRoot = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (syntaxRoot is null || semanticModel is null)
        {
            return Failure<SourceContextSnippet>("RoslynQueryFailed: document has no syntax root or semantic model.");
        }

        var token = syntaxRoot.FindToken(absolutePosition.Value);
        var node = token.Parent;
        var sourceNode = FindSourceContextNode(node) ?? node ?? syntaxRoot;
        var focusSpan = new TextSpan(absolutePosition.Value, 0);
        var symbol = FindContainingDeclaredSymbol(sourceNode, semanticModel, cancellationToken)
            ?? semanticModel.GetDeclaredSymbol(sourceNode, cancellationToken);
        var descriptor = symbol is null ? null : CreateDescriptor(symbol, document.Project, cancellationToken);
        var snippet = CreateSourceContextSnippet(
            document,
            sourceText,
            focusSpan,
            sourceNode.Span,
            descriptor,
            DescribeSourceContextKind(sourceNode),
            request.ContextLines,
            request.MaxChars,
            new[] { "SourcePosition", DescribeSourceContextKind(sourceNode) });

        return Success(new[] { snippet }, isPartial: snippet.IsTextTruncated);
    }

    public async Task<WorkspaceQueryResult<SourceContextSnippet>> GetSourceContextsAsync(
        BatchSourceContextRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Positions is null || request.Positions.Length == 0)
        {
            return Failure<SourceContextSnippet>("Positions is required.");
        }

        if (request.Positions.Length > 100)
        {
            return Failure<SourceContextSnippet>("Positions cannot contain more than 100 items.");
        }

        if (request.ContextLines is < 0 or > 50 || request.MaxCharsPerPosition is < 200 or > 100000)
        {
            return Failure<SourceContextSnippet>("Batch source context limits are out of range.");
        }

        var snippets = new List<SourceContextSnippet>();
        var diagnostics = new List<string>();
        var isPartial = false;
        var solutionResult = await GetRequiredSolutionAsync(cancellationToken).ConfigureAwait(false);
        if (solutionResult.Failure is not null)
        {
            return solutionResult.Failure.As<SourceContextSnippet>();
        }

        var solution = solutionResult.Solution!;
        var workspaceVersion = WorkspaceSnapshotIdentity.GetVersion(solution);
        if (!string.IsNullOrWhiteSpace(request.ExpectedWorkspaceVersion)
            && !string.Equals(request.ExpectedWorkspaceVersion, workspaceVersion, StringComparison.Ordinal))
        {
            return Failure<SourceContextSnippet>(
                $"WorkspaceVersionChanged: expected '{request.ExpectedWorkspaceVersion}' but active snapshot is '{workspaceVersion}'.");
        }
        diagnostics.Add($"WorkspaceSnapshotVersion: {workspaceVersion}");
        foreach (var position in request.Positions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await GetSourceContextFromSolutionAsync(
                    new SourceContextRequest
                    {
                        Target = request.Target,
                        Position = new SourceSpan
                        {
                            FilePath = position.FilePath,
                            StartLine = position.Line,
                            StartColumn = position.Column,
                        },
                        ContextLines = request.ContextLines,
                        MaxChars = request.MaxCharsPerPosition,
                        MaxSnippets = 1,
                        IncludeGeneratedCode = request.IncludeGeneratedCode,
                    },
                    solution,
                    cancellationToken)
                .ConfigureAwait(false);
            snippets.AddRange(result.Items);
            diagnostics.AddRange(result.Diagnostics.Select(diagnostic =>
                $"SourceContext[{position.FilePath}:{position.Line}:{position.Column}]: {diagnostic}"));
            isPartial |= result.IsPartial;
        }

        return Success(snippets.ToArray(), diagnostics, isPartial);
    }

    public async Task<WorkspaceQueryResult<DocumentSymbolNode>> ListDocumentSymbolsAsync(
        DocumentSymbolsRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.FilePath))
        {
            return Failure<DocumentSymbolNode>("FilePath is required.");
        }

        if (request.MaxResults is < 1 or > 5000)
        {
            return Failure<DocumentSymbolNode>("MaxResults must be between 1 and 5000.");
        }

        var solutionResult = await GetRequiredSolutionAsync(cancellationToken).ConfigureAwait(false);
        if (solutionResult.Failure is not null)
        {
            return solutionResult.Failure.As<DocumentSymbolNode>();
        }

        var document = await FindDocumentByPathAsync(
                solutionResult.Solution!,
                request.FilePath,
                request.IncludeGeneratedCode,
                cancellationToken)
            .ConfigureAwait(false);
        if (document is null)
        {
            return Failure<DocumentSymbolNode>("DocumentNotFound: the requested file is not in the active solution.");
        }

        if (!request.IncludeGeneratedCode && IsGeneratedDocument(document))
        {
            return Failure<DocumentSymbolNode>("The requested document is generated code. Retry with IncludeGeneratedCode=true.");
        }

        var syntaxRoot = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (syntaxRoot is null || semanticModel is null)
        {
            return Failure<DocumentSymbolNode>("RoslynQueryFailed: document has no syntax root or semantic model.");
        }

        var nodes = CreateDocumentSymbolNodes(
            syntaxRoot,
            semanticModel,
            document.Project,
            request.MaxResults,
            cancellationToken);

        return Success(nodes.Items, nodes.Diagnostics, nodes.IsPartial);
    }

}
