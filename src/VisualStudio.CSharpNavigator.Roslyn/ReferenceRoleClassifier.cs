using VisualStudio.CSharpNavigator.Protocol;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace VisualStudio.CSharpNavigator.Roslyn;

public static class ReferenceRoleClassifier
{
    public static ReferenceRole Classify(SyntaxNode? syntaxRoot, TextSpan sourceSpan)
    {
        if (syntaxRoot is null)
        {
            return ReferenceRole.Unknown;
        }

        var node = syntaxRoot.FindNode(sourceSpan, getInnermostNodeForTie: true);
        if (node.FirstAncestorOrSelf<AssignmentExpressionSyntax>() is { } assignment
            && assignment.Left.Span.Contains(sourceSpan))
        {
            return ReferenceRole.Write;
        }

        if (node.FirstAncestorOrSelf<PrefixUnaryExpressionSyntax>() is { } prefixUnary
            && (prefixUnary.IsKind(SyntaxKind.PreIncrementExpression)
                || prefixUnary.IsKind(SyntaxKind.PreDecrementExpression)))
        {
            return ReferenceRole.Write;
        }

        if (node.FirstAncestorOrSelf<PostfixUnaryExpressionSyntax>() is not null)
        {
            return ReferenceRole.Write;
        }

        if (node.FirstAncestorOrSelf<ArgumentSyntax>() is { } argument
            && argument.Expression.Span.Contains(sourceSpan)
            && (argument.RefOrOutKeyword.IsKind(SyntaxKind.OutKeyword)
                || argument.RefOrOutKeyword.IsKind(SyntaxKind.RefKeyword)))
        {
            return ReferenceRole.Write;
        }

        if (node.FirstAncestorOrSelf<InvocationExpressionSyntax>() is { } invocation
            && IsInvocationTarget(invocation.Expression, sourceSpan))
        {
            return ReferenceRole.Invocation;
        }

        if (node.FirstAncestorOrSelf<ObjectCreationExpressionSyntax>() is { } objectCreation
            && IsObjectCreationTarget(objectCreation.Type, sourceSpan))
        {
            return ReferenceRole.Invocation;
        }

        return ReferenceRole.Read;
    }

    private static bool IsInvocationTarget(ExpressionSyntax expression, TextSpan sourceSpan)
    {
        switch (expression)
        {
            case MemberAccessExpressionSyntax memberAccess:
                return memberAccess.Name.Span.Contains(sourceSpan);
            case MemberBindingExpressionSyntax memberBinding:
                return memberBinding.Name.Span.Contains(sourceSpan);
            default:
                return expression.Span.Contains(sourceSpan);
        }
    }

    private static bool IsObjectCreationTarget(TypeSyntax type, TextSpan sourceSpan)
    {
        switch (type)
        {
            case IdentifierNameSyntax identifierName:
                return identifierName.Identifier.Span.Contains(sourceSpan);
            case GenericNameSyntax genericName:
                return genericName.Identifier.Span.Contains(sourceSpan);
            case QualifiedNameSyntax qualifiedName:
                return IsObjectCreationTarget(qualifiedName.Right, sourceSpan);
            case AliasQualifiedNameSyntax aliasQualifiedName:
                return aliasQualifiedName.Name.Identifier.Span.Contains(sourceSpan);
            case PredefinedTypeSyntax predefinedType:
                return predefinedType.Keyword.Span.Contains(sourceSpan);
            default:
                return false;
        }
    }
}
