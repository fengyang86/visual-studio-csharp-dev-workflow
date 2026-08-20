using VisualStudio.CSharpNavigator.Abstractions;
using VisualStudio.CSharpNavigator.Server.Bridge;
using VisualStudio.CSharpNavigator.Server.Agentic;
using VisualStudio.CSharpNavigator.Server.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;

var builder = Host.CreateApplicationBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Information);

var evidenceStore = new EvidenceStore();
var evidenceResourceHandlers = new EvidenceResourceHandlers(evidenceStore);

builder.Services.Configure<NamedPipeBridgeOptions>(
    builder.Configuration.GetSection(NamedPipeBridgeOptions.ConfigurationSectionName));
builder.Services.AddSingleton<BridgeCallTelemetryRecorder>();
builder.Services.AddSingleton<IVisualStudioWorkspaceBridge, NamedPipeVisualStudioWorkspaceBridge>();
builder.Services.AddSingleton<ShortLivedQueryCache>();
builder.Services.AddSingleton<IVisualStudioSolutionLauncher, VisualStudioSolutionLauncher>();
builder.Services.AddSingleton<CodeNavigationTools>();
builder.Services.AddSingleton<WorkspacePreparationTools>();
builder.Services.AddSingleton<WorkspaceHealthTools>();
builder.Services.AddSingleton<CodeIntelligenceTools>();
builder.Services.AddSingleton<BuildDiagnosticsTools>();
builder.Services.AddSingleton<ReviewVerificationTools>();
builder.Services.AddSingleton<VisualStudioDocumentTools>();
builder.Services.AddSingleton<CodeCleanupTools>();
builder.Services.AddSingleton<CodeFixTools>();
builder.Services.AddSingleton<RefactoringTools>();
builder.Services.AddSingleton<DebugContextTools>();
builder.Services.AddSingleton<DebugControlTools>();
builder.Services.AddSingleton<AgenticWorkflowTools>();
builder.Services.AddSingleton<WorkflowEnhancementTools>();
builder.Services.AddSingleton<WorkflowKernel>();
builder.Services.AddSingleton(evidenceStore);
builder.Services.AddSingleton<MutationSessionStore>();
builder.Services.AddSingleton(evidenceResourceHandlers);
builder.Services.AddSingleton<EvidencePacketBuilder>();
builder.Services.AddSingleton<WorkflowBudgetPolicy>();
builder.Services.AddSingleton<WorkflowSafetyGate>();
builder.Services.AddSingleton<TaskRouter>();
builder.Services.AddSingleton<WorkflowTelemetryRecorder>();
builder.Services.AddSingleton<WorkspaceContextLeaseStore>();

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly(typeof(CodeNavigationTools).Assembly)
    .WithListResourcesHandler((request, _) =>
    {
        return ValueTask.FromResult(evidenceResourceHandlers.ListResources());
    })
    .WithReadResourceHandler((request, _) =>
    {
        return ValueTask.FromResult(evidenceResourceHandlers.ReadResource(request.Params?.Uri ?? string.Empty));
    })
    .WithListResourceTemplatesHandler((request, _) =>
    {
        return ValueTask.FromResult(evidenceResourceHandlers.ListResourceTemplates());
    });

await builder.Build().RunAsync();
