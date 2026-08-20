using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace CodeNavigator.Sample.Generator;

[Generator]
public sealed class GeneratedGreeterGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        context.RegisterPostInitializationOutput(static output =>
        {
            const string source = """
namespace CodeNavigator.Sample.Generated;

public static class GeneratedGreeter
{
    public static string Message => "Hello from a source generator.";
}
""";

            output.AddSource("GeneratedGreeter.g.cs", SourceText.From(source, Encoding.UTF8));
        });
    }
}
