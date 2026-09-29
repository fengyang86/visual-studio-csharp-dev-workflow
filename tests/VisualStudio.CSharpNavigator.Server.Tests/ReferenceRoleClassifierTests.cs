using VisualStudio.CSharpNavigator.Protocol;
using VisualStudio.CSharpNavigator.Roslyn;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace VisualStudio.CSharpNavigator.Server.Tests;

public sealed class ReferenceRoleClassifierTests
{
    [Fact]
    public void Classify_ReturnsRead_ForInvocationArguments()
    {
        var syntax = CSharpSyntaxTree.ParseText("""
            class Sample
            {
                void Test()
                {
                    var line = new LcLine(item.Start, item.End);
                }
            }
            """);

        Assert.Equal(ReferenceRole.Read, ClassifyToken(syntax, "Start"));
        Assert.Equal(ReferenceRole.Read, ClassifyToken(syntax, "End"));
        Assert.Equal(ReferenceRole.Invocation, ClassifyToken(syntax, "LcLine"));
    }

    [Fact]
    public void Classify_ReturnsRead_ForInvocationReceiverReads()
    {
        var syntax = CSharpSyntaxTree.ParseText("""
            class Sample
            {
                void Test()
                {
                    var angle = (this.End - this.Start).Angle();
                }
            }
            """);

        Assert.Equal(ReferenceRole.Read, ClassifyToken(syntax, "End"));
        Assert.Equal(ReferenceRole.Read, ClassifyToken(syntax, "Start"));
        Assert.Equal(ReferenceRole.Invocation, ClassifyToken(syntax, "Angle"));
    }

    [Fact]
    public void Classify_ReturnsRead_ForGenericTypeArgumentsInsideObjectCreation()
    {
        var syntax = CSharpSyntaxTree.ParseText("""
            using System.Collections.Generic;

            class Sample
            {
                void Test()
                {
                    var list = new List<Foo>();
                }
            }
            """);

        Assert.Equal(ReferenceRole.Invocation, ClassifyToken(syntax, "List"));
        Assert.Equal(ReferenceRole.Read, ClassifyToken(syntax, "Foo"));
    }

    [Fact]
    public void Classify_ReturnsWrite_ForIncrementAndRefOutArguments()
    {
        var syntax = CSharpSyntaxTree.ParseText("""
            class Sample
            {
                void Test()
                {
                    ++value;
                    value++;
                    Callee(ref value, out result);
                }

                void Callee(ref int a, out int b)
                {
                    a = 0;
                    b = 0;
                }
            }
            """);

        Assert.Equal(ReferenceRole.Write, ClassifyToken(syntax, "value", 0));
        Assert.Equal(ReferenceRole.Write, ClassifyToken(syntax, "value", 1));
        Assert.Equal(ReferenceRole.Write, ClassifyToken(syntax, "value", 2));
        Assert.Equal(ReferenceRole.Write, ClassifyToken(syntax, "result"));
    }

    [Fact]
    public void Classify_ReturnsRead_ForUnaryOperatorsThatAreNotWrites()
    {
        var syntax = CSharpSyntaxTree.ParseText("""
            class Sample
            {
                void Test()
                {
                    var a = !flag;
                    var b = -number;
                    var c = ~bits;
                }
            }
            """);

        Assert.Equal(ReferenceRole.Read, ClassifyToken(syntax, "flag"));
        Assert.Equal(ReferenceRole.Read, ClassifyToken(syntax, "number"));
        Assert.Equal(ReferenceRole.Read, ClassifyToken(syntax, "bits"));
    }

    [Fact]
    public void Classify_ReturnsUnknown_WhenSyntaxRootMissing()
    {
        var role = ReferenceRoleClassifier.Classify(null, default);

        Assert.Equal(ReferenceRole.Unknown, role);
    }

    [Fact]
    public void Classify_ReturnsRead_ForAssignmentReceiversAndIndexes()
    {
        var syntax = CSharpSyntaxTree.ParseText("""
            class Sample
            {
                void Test()
                {
                    Config.Name = "x";
                    Items[index] = value;
                    Outer.Inner.Flag = true;
                }
            }
            """);

        Assert.Equal(ReferenceRole.Read, ClassifyToken(syntax, "Config"));
        Assert.Equal(ReferenceRole.Write, ClassifyToken(syntax, "Name"));
        Assert.Equal(ReferenceRole.Read, ClassifyToken(syntax, "Items"));
        Assert.Equal(ReferenceRole.Read, ClassifyToken(syntax, "index"));
        Assert.Equal(ReferenceRole.Read, ClassifyToken(syntax, "Outer"));
        Assert.Equal(ReferenceRole.Read, ClassifyToken(syntax, "Inner"));
        Assert.Equal(ReferenceRole.Write, ClassifyToken(syntax, "Flag"));
    }

    [Fact]
    public void Classify_ReturnsRead_ForRefOutReceiversAndIndexes()
    {
        var syntax = CSharpSyntaxTree.ParseText("""
            class Sample
            {
                void Test()
                {
                    map.TryGetValue(key, out box.Score);
                    parser.Read(out slots[index]);
                }
            }
            """);

        Assert.Equal(ReferenceRole.Read, ClassifyToken(syntax, "map"));
        Assert.Equal(ReferenceRole.Read, ClassifyToken(syntax, "key"));
        Assert.Equal(ReferenceRole.Write, ClassifyToken(syntax, "Score"));
        Assert.Equal(ReferenceRole.Read, ClassifyToken(syntax, "box"));
        Assert.Equal(ReferenceRole.Read, ClassifyToken(syntax, "slots"));
        Assert.Equal(ReferenceRole.Read, ClassifyToken(syntax, "index"));
    }

    [Fact]
    public void Classify_ReturnsWrite_OnlyForMemberAccessIncrementTarget()
    {
        var syntax = CSharpSyntaxTree.ParseText("""
            class Sample
            {
                void Test()
                {
                    counter.Total++;
                    --state.RetryCount;
                }
            }
            """);

        Assert.Equal(ReferenceRole.Read, ClassifyToken(syntax, "counter"));
        Assert.Equal(ReferenceRole.Write, ClassifyToken(syntax, "Total"));
        Assert.Equal(ReferenceRole.Read, ClassifyToken(syntax, "state"));
        Assert.Equal(ReferenceRole.Write, ClassifyToken(syntax, "RetryCount"));
    }

    [Fact]
    public void Classify_ReturnsRead_ForSuppressionOperator()
    {
        var syntax = CSharpSyntaxTree.ParseText("""
            class Sample
            {
                void Test()
                {
                    var current = handler!;
                }
            }
            """);

        Assert.Equal(ReferenceRole.Read, ClassifyToken(syntax, "handler"));
    }

    private static ReferenceRole ClassifyToken(SyntaxTree syntaxTree, string tokenText, int occurrenceIndex = 0)
    {
        var root = syntaxTree.GetRoot();
        var token = root.DescendantTokens().Where(candidate => candidate.ValueText == tokenText).ElementAtOrDefault(occurrenceIndex);
        if (token == default)
        {
            throw new InvalidOperationException($"Token '{tokenText}' occurrence {occurrenceIndex} was not found in sample syntax.");
        }

        return ReferenceRoleClassifier.Classify(root, token.Span);
    }
}
