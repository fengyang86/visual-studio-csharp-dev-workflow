using ModelContextProtocol.Protocol;

namespace VisualStudio.CSharpNavigator.Server.Agentic;

public sealed class EvidenceResourceHandlers
{
    private readonly EvidenceStore _evidenceStore;

    public EvidenceResourceHandlers(EvidenceStore evidenceStore)
    {
        _evidenceStore = evidenceStore;
    }

    public ListResourcesResult ListResources()
    {
        return new ListResourcesResult
        {
            Resources = _evidenceStore.ListResources(),
        };
    }

    public ReadResourceResult ReadResource(string uri)
    {
        return _evidenceStore.ReadResource(uri);
    }

    public ListResourceTemplatesResult ListResourceTemplates()
    {
        return new ListResourceTemplatesResult
        {
            ResourceTemplates = new ResourceTemplate[]
            {
                new()
                {
                    UriTemplate = "csharp://task/{taskId}/context",
                    Name = "task-context",
                    Title = "Task context summary",
                    Description = "Compact Agentic C# workflow task context.",
                    MimeType = "text/plain",
                },
                new()
                {
                    UriTemplate = "csharp://task/{taskId}/context.json",
                    Name = "task-context-json",
                    Title = "Task context JSON",
                    Description = "Machine-readable Agentic C# workflow task context.",
                    MimeType = "application/json",
                },
                new()
                {
                    UriTemplate = "csharp://task/{taskId}/source/{index}",
                    Name = "task-source",
                    Title = "Task source snippet",
                    Description = "Bounded source snippet captured for an Agentic C# workflow task.",
                    MimeType = "text/plain",
                },
                new()
                {
                    UriTemplate = "csharp://task/{taskId}/review",
                    Name = "change-review",
                    Title = "Change review summary",
                    Description = "Compact Agentic C# workflow change review.",
                    MimeType = "text/plain",
                },
                new()
                {
                    UriTemplate = "csharp://task/{taskId}/review.json",
                    Name = "change-review-json",
                    Title = "Change review JSON",
                    Description = "Machine-readable Agentic C# workflow change review.",
                    MimeType = "application/json",
                },
                new()
                {
                    UriTemplate = "csharp://task/{taskId}/verification",
                    Name = "verification-run",
                    Title = "Verification run summary",
                    Description = "Compact Agentic C# workflow verification run plan.",
                    MimeType = "text/plain",
                },
                new()
                {
                    UriTemplate = "csharp://task/{taskId}/verification.json",
                    Name = "verification-run-json",
                    Title = "Verification run JSON",
                    Description = "Machine-readable Agentic C# workflow verification run plan.",
                    MimeType = "application/json",
                },
                new()
                {
                    UriTemplate = "csharp://task/{taskId}/debug-evidence",
                    Name = "debug-evidence",
                    Title = "Runtime exception debug evidence",
                    Description = "Compact Agentic C# workflow runtime exception evidence.",
                    MimeType = "text/plain",
                },
                new()
                {
                    UriTemplate = "csharp://task/{taskId}/debug-evidence.json",
                    Name = "debug-evidence-json",
                    Title = "Runtime exception debug evidence JSON",
                    Description = "Machine-readable Agentic C# workflow runtime exception evidence.",
                    MimeType = "application/json",
                },
                new()
                {
                    UriTemplate = "csharp://task/{taskId}/build-failure",
                    Name = "build-failure",
                    Title = "Build failure session",
                    Description = "Compact Agentic C# workflow build failure evidence.",
                    MimeType = "text/plain",
                },
                new()
                {
                    UriTemplate = "csharp://task/{taskId}/build-failure.json",
                    Name = "build-failure-json",
                    Title = "Build failure session JSON",
                    Description = "Machine-readable Agentic C# workflow build failure evidence.",
                    MimeType = "application/json",
                },
                new()
                {
                    UriTemplate = "csharp://task/{taskId}/build-log/{issueId}",
                    Name = "build-log",
                    Title = "Build log evidence",
                    Description = "Compact build-log triage evidence for an Agentic C# workflow task.",
                    MimeType = "text/plain",
                },
                new()
                {
                    UriTemplate = "csharp://task/{taskId}/diagnostics",
                    Name = "diagnostics",
                    Title = "Scoped diagnostics",
                    Description = "Compact scoped Roslyn diagnostics for an Agentic C# workflow task.",
                    MimeType = "text/plain",
                },
                new()
                {
                    UriTemplate = "csharp://task/{taskId}/impact-graph",
                    Name = "impact-graph",
                    Title = "Symbol impact graph",
                    Description = "Compact symbol impact graph for an Agentic C# workflow task.",
                    MimeType = "text/plain",
                },
                new()
                {
                    UriTemplate = "csharp://task/{taskId}/artifact/{artifactId}",
                    Name = "artifact",
                    Title = "Artifact evidence",
                    Description = "Compact artifact evidence for an Agentic C# workflow task.",
                    MimeType = "text/plain",
                },
            },
        };
    }
}
