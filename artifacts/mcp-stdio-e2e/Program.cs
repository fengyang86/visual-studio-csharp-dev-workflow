using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

var workspaceRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
var configuredServerExe = Environment.GetEnvironmentVariable("CODE_NAVIGATOR_SERVER_EXE");
#if DEBUG
const string defaultServerConfiguration = "Debug";
#else
const string defaultServerConfiguration = "Release";
#endif
var serverExe = string.IsNullOrWhiteSpace(configuredServerExe)
    ? Path.Combine(
        workspaceRoot,
        "src",
        "VisualStudio.CSharpNavigator.Server",
        "bin",
        defaultServerConfiguration,
        "net8.0",
        "VisualStudio.CSharpNavigator.Server.exe")
    : Path.GetFullPath(configuredServerExe);

if (!File.Exists(serverExe))
{
    throw new FileNotFoundException("Build or publish the MCP server before running the E2E client.", serverExe);
}

var profile = TestProfile.Load(workspaceRoot);

var transport = new StdioClientTransport(new StdioClientTransportOptions
{
    Name = "VisualStudio.CSharpNavigator MCP E2E",
    Command = serverExe,
    WorkingDirectory = workspaceRoot,
    EnvironmentVariables = new Dictionary<string, string?>
    {
        ["VisualStudioBridge__SolutionPath"] = profile.SolutionPath,
        ["VisualStudioBridge__ConnectTimeoutMilliseconds"] =
            Environment.GetEnvironmentVariable("VisualStudioBridge__ConnectTimeoutMilliseconds") ?? "5000",
        ["VisualStudioBridge__DiscoveryStaleAfterSeconds"] =
            Environment.GetEnvironmentVariable("VisualStudioBridge__DiscoveryStaleAfterSeconds") ?? "120",
    },
    StandardErrorLines = line => Console.Error.WriteLine("[server] " + line),
});

await using var client = await McpClient.CreateAsync(transport);

var tools = await client.ListToolsAsync();
var toolNames = tools.Select(tool => tool.Name).OrderBy(name => name).ToArray();
Console.WriteLine("TOOLS " + string.Join(",", toolNames));

var resources = await client.ListResourcesAsync();
var resourceTemplates = await client.ListResourceTemplatesAsync();
Console.WriteLine("RESOURCES count={0} templates={1}", resources.Count, resourceTemplates.Count);

if (profile.IsToolSchemaProfile)
{
    RunToolSchemaSmoke(toolNames, tools, resourceTemplates);
    return;
}

if (profile.IsAgenticResourcesProfile)
{
    await RunAgenticResourcesSmokeAsync(client);
    return;
}

if (profile.IsOutputWindowProfile)
{
    await RunOutputWindowSmokeAsync(client, profile);
    return;
}

if (profile.IsCodeFixProfile)
{
    await RunCodeFixSmokeAsync(client, profile);
    return;
}

if (profile.IsWorkflowProfile)
{
    await RunWorkflowSmokeAsync(client, profile);
    return;
}

if (profile.IsDebugControlProfile)
{
    await RunDebugControlSmokeAsync(client, profile);
    return;
}

if (!profile.IsCSharpNavigatorProfile)
{
    await RunGenericSmokeAsync(client, profile);
    return;
}

var status = await CallAsync(client, "get_csharp_workspace_status", profile.WithTarget(new Dictionary<string, object?>()));
using (var payload = ParseToolPayload(status))
{
    var items = payload.RootElement.GetProperty("items");
    if (items.GetArrayLength() == 0)
    {
        throw new InvalidOperationException("Workspace status returned no items. Wait for Visual Studio to finish loading the solution and retry.");
    }

    var item = items[0];
    Console.WriteLine(
        "STATUS solutionPath={0} projects={1} documents={2} isPartial={3}",
        item.GetProperty("solutionPath").GetString(),
        item.GetProperty("projectCount").GetInt32(),
        item.GetProperty("documentCount").GetInt32(),
        payload.RootElement.GetProperty("isPartial").GetBoolean());
}

var search = await CallAsync(client, "search_csharp_symbols", profile.WithTarget(new Dictionary<string, object?>
{
    ["queryText"] = "SetProps",
    ["maxResults"] = 10,
    ["includeGeneratedCode"] = false,
}));
using (var payload = ParseToolPayload(search))
{
    var first = payload.RootElement.GetProperty("items")[0];
    Console.WriteLine(
        "SEARCH count={0} first={1}.{2} file={3} line={4}",
        payload.RootElement.GetProperty("items").GetArrayLength(),
        first.GetProperty("containingType").GetString(),
        first.GetProperty("name").GetString(),
        first.GetProperty("span").GetProperty("filePath").GetString(),
        first.GetProperty("span").GetProperty("startLine").GetInt32());
}

var generatedExcluded = await CallAsync(client, "search_csharp_symbols", profile.WithTarget(new Dictionary<string, object?>
{
    ["queryText"] = "toolStripSeparator2",
    ["maxResults"] = 10,
    ["includeGeneratedCode"] = false,
}));
var generatedIncluded = await CallAsync(client, "search_csharp_symbols", profile.WithTarget(new Dictionary<string, object?>
{
    ["queryText"] = "toolStripSeparator2",
    ["maxResults"] = 10,
    ["includeGeneratedCode"] = true,
}));
using (var excludedPayload = ParseToolPayload(generatedExcluded))
using (var includedPayload = ParseToolPayload(generatedIncluded))
{
    var excludedCount = excludedPayload.RootElement.GetProperty("items").GetArrayLength();
    var includedItems = includedPayload.RootElement.GetProperty("items");
    var includedCount = includedItems.GetArrayLength();
    Console.WriteLine(
        "GENERATED_CHECK query=toolStripSeparator2 excludedCount={0} includedCount={1} includedIsPartial={2}",
        excludedCount,
        includedCount,
        includedPayload.RootElement.GetProperty("isPartial").GetBoolean());

    if (includedCount > 0)
    {
        var first = includedItems[0];
        Console.WriteLine(
            "GENERATED_FIRST name={0} kind={1} file={2} line={3}",
            first.GetProperty("name").GetString(),
            first.GetProperty("kind").GetString(),
            first.GetProperty("span").GetProperty("filePath").GetString(),
            first.GetProperty("span").GetProperty("startLine").GetInt32());
    }
}

var symbolKey = ExtractFirstSymbolKey(search);
Console.WriteLine("SYMBOL_KEY " + symbolKey);

var definitions = await CallAsync(client, "find_csharp_definitions", profile.WithTarget(new Dictionary<string, object?>
{
    ["symbolKey"] = symbolKey,
    ["includeGeneratedCode"] = false,
}));
using (var payload = ParseToolPayload(definitions))
{
    Console.WriteLine("DEFINITIONS count={0}", payload.RootElement.GetProperty("items").GetArrayLength());
}

var references = await CallAsync(client, "find_csharp_references", profile.WithTarget(new Dictionary<string, object?>
{
    ["symbolKey"] = symbolKey,
    ["includeGeneratedCode"] = false,
}));
using (var payload = ParseToolPayload(references))
{
    var first = payload.RootElement.GetProperty("items")[0];
    Console.WriteLine(
        "REFERENCES count={0} firstFile={1} firstLine={2}",
        payload.RootElement.GetProperty("items").GetArrayLength(),
        first.GetProperty("span").GetProperty("filePath").GetString(),
        first.GetProperty("span").GetProperty("startLine").GetInt32());
}

var description = await CallAsync(client, "describe_csharp_symbol", profile.WithTarget(new Dictionary<string, object?>
{
    ["symbolKey"] = symbolKey,
    ["includeGeneratedCode"] = false,
}));
using (var payload = ParseToolPayload(description))
{
    var first = payload.RootElement.GetProperty("items")[0];
    Console.WriteLine(
        "DESCRIBE name={0} display={1} accessibility={2} static={3}",
        first.GetProperty("symbol").GetProperty("name").GetString(),
        first.GetProperty("displayString").GetString(),
        first.GetProperty("accessibility").GetString(),
        first.GetProperty("isStatic").GetBoolean());
}

var documentSymbols = await CallAsync(client, "list_csharp_document_symbols", profile.WithTarget(new Dictionary<string, object?>
{
    ["filePath"] = profile.DocumentPath,
    ["maxResults"] = 200,
    ["includeGeneratedCode"] = false,
}));
using (var payload = ParseToolPayload(documentSymbols))
{
    var items = payload.RootElement.GetProperty("items");
    Console.WriteLine(
        "DOC_SYMBOLS file=LcLine.cs count={0} partial={1}",
        items.GetArrayLength(),
        payload.RootElement.GetProperty("isPartial").GetBoolean());
    PrintFirstDocumentSymbolSample(items, "LcLine");
}

var diagnostics = await CallAsync(client, "get_csharp_diagnostics", profile.WithTarget(new Dictionary<string, object?>
{
    ["filePath"] = profile.DocumentPath,
    ["minimumSeverity"] = "Warning",
    ["maxResults"] = 20,
    ["includeGeneratedCode"] = false,
}));
using (var payload = ParseToolPayload(diagnostics))
{
    Console.WriteLine(
        "DIAGNOSTICS file=LcLine.cs count={0} partial={1}",
        payload.RootElement.GetProperty("items").GetArrayLength(),
        payload.RootElement.GetProperty("isPartial").GetBoolean());
}

var callers = await CallAsync(client, "find_csharp_callers", profile.WithTarget(new Dictionary<string, object?>
{
    ["symbolKey"] = symbolKey,
    ["maxDepth"] = 1,
    ["maxResults"] = 25,
    ["includeGeneratedCode"] = false,
}));
using (var payload = ParseToolPayload(callers))
{
    var items = payload.RootElement.GetProperty("items");
    Console.WriteLine(
        "CALLERS query=SetProps count={0} partial={1}",
        items.GetArrayLength(),
        payload.RootElement.GetProperty("isPartial").GetBoolean());
    PrintFirstCallGraphSample("CALLERS_SAMPLE", "SetProps", items);
}

var callees = await CallAsync(client, "find_csharp_callees", profile.WithTarget(new Dictionary<string, object?>
{
    ["symbolKey"] = symbolKey,
    ["maxDepth"] = 1,
    ["maxResults"] = 25,
    ["includeGeneratedCode"] = false,
}));
using (var payload = ParseToolPayload(callees))
{
    var items = payload.RootElement.GetProperty("items");
    Console.WriteLine(
        "CALLEES query=SetProps count={0} partial={1}",
        items.GetArrayLength(),
        payload.RootElement.GetProperty("isPartial").GetBoolean());
    PrintFirstCallGraphSample("CALLEES_SAMPLE", "SetProps", items);
}

var impact = await CallAsync(client, "analyze_csharp_symbol_impact", profile.WithTarget(new Dictionary<string, object?>
{
    ["symbolKey"] = symbolKey,
    ["maxResults"] = 2000,
    ["maxProjects"] = 10,
    ["maxFiles"] = 10,
    ["maxContainingTypes"] = 10,
    ["includeGeneratedCode"] = false,
}));
using (var payload = ParseToolPayload(impact))
{
    var items = payload.RootElement.GetProperty("items");
    if (items.GetArrayLength() == 0)
    {
        throw new InvalidOperationException("Impact analysis returned no items: " + payload.RootElement.ToString());
    }

    var item = items[0];
    Console.WriteLine(
        "IMPACT query=SetProps total={0} projects={1} files={2} containingTypes={3} crossProject={4} partial={5}",
        item.GetProperty("totalReferences").GetInt32(),
        item.GetProperty("distinctProjectCount").GetInt32(),
        item.GetProperty("distinctFileCount").GetInt32(),
        item.GetProperty("distinctContainingTypeCount").GetInt32(),
        item.GetProperty("hasCrossProjectImpact").GetBoolean(),
        payload.RootElement.GetProperty("isPartial").GetBoolean());
    PrintFirstImpactFileSample("IMPACT_TOP_FILE", "SetProps", item.GetProperty("files"));
}

var lcElementKey = await SearchSymbolKeyAsync(client, profile, "LcElement", "LcElement", string.Empty);
var derivedTypes = await CallAsync(client, "find_csharp_derived_types", profile.WithTarget(new Dictionary<string, object?>
{
    ["symbolKey"] = lcElementKey,
    ["transitive"] = true,
    ["maxResults"] = 25,
    ["includeGeneratedCode"] = false,
}));
using (var payload = ParseToolPayload(derivedTypes))
{
    var items = payload.RootElement.GetProperty("items");
    Console.WriteLine(
        "DERIVED_TYPES query=LcElement count={0} partial={1} diagnostics={2}",
        items.GetArrayLength(),
        payload.RootElement.GetProperty("isPartial").GetBoolean(),
        payload.RootElement.GetProperty("diagnostics").GetArrayLength());
    PrintDiagnostics("DERIVED_TYPES_DIAGNOSTIC", payload.RootElement.GetProperty("diagnostics"));
    PrintFirstDerivedTypeSample("DERIVED_TYPES_SAMPLE", "LcElement", items);
}

var lcLineKey = await SearchSymbolKeyAsync(client, profile, "LcLine", "LcLine", string.Empty);
var inheritance = await CallAsync(client, "get_csharp_inheritance_chain", profile.WithTarget(new Dictionary<string, object?>
{
    ["symbolKey"] = lcLineKey,
    ["includeGeneratedCode"] = false,
}));
using (var payload = ParseToolPayload(inheritance))
{
    var items = payload.RootElement.GetProperty("items");
    if (items.GetArrayLength() == 0)
    {
        PrintDiagnostics("INHERITANCE_DIAGNOSTIC", payload.RootElement.GetProperty("diagnostics"));
        throw new InvalidOperationException("Inheritance chain returned no items.");
    }

    var item = items[0];
    Console.WriteLine(
        "INHERITANCE query=LcLine bases={0} directInterfaces={1} allInterfaces={2}",
        item.GetProperty("baseTypes").GetArrayLength(),
        item.GetProperty("directInterfaces").GetArrayLength(),
        item.GetProperty("allInterfaces").GetArrayLength());
}

var relatedTests = await CallAsync(client, "find_csharp_related_tests", profile.WithTarget(new Dictionary<string, object?>
{
    ["symbolKey"] = lcLineKey,
    ["maxResults"] = 10,
    ["includeGeneratedCode"] = false,
}));
using (var payload = ParseToolPayload(relatedTests))
{
    var items = payload.RootElement.GetProperty("items");
    if (DiagnosticsContain(payload, "UnsupportedMethod"))
    {
        PrintDiagnostics("RELATED_TESTS_DIAGNOSTIC", payload.RootElement.GetProperty("diagnostics"));
        throw new InvalidOperationException("Related tests query was not supported by the loaded VSIX bridge.");
    }

    if (!DiagnosticsContain(payload, "Related test discovery"))
    {
        throw new InvalidOperationException("Related tests query did not disclose reference-vs-heuristic evidence.");
    }

    Console.WriteLine(
        "RELATED_TESTS query=LcLine count={0} partial={1} diagnostics={2}",
        items.GetArrayLength(),
        payload.RootElement.GetProperty("isPartial").GetBoolean(),
        payload.RootElement.GetProperty("diagnostics").GetArrayLength());
    PrintDiagnostics("RELATED_TESTS_DIAGNOSTIC", payload.RootElement.GetProperty("diagnostics"));
    PrintFirstRelatedTestSample("RELATED_TESTS_SAMPLE", "LcLine", items);
}

var lcLineStartKeyForRename = await SearchSymbolKeyAsync(client, profile, "LcLine.Start", "Start", "SampleWorkspace.Core.Elements.LcLine");
var renamePreview = await CallAsync(client, "preview_csharp_rename", profile.WithTarget(new Dictionary<string, object?>
{
    ["symbolKey"] = lcLineStartKeyForRename,
    ["newName"] = "StartPreview",
    ["renameOverloads"] = false,
    ["renameInStrings"] = false,
    ["renameInComments"] = false,
    ["renameFile"] = false,
    ["maxTextChanges"] = 10,
    ["maxSnippetLength"] = 80,
    ["includeGeneratedCode"] = false,
}));
using (var payload = ParseToolPayload(renamePreview))
{
    if (DiagnosticsContain(payload, "UnsupportedMethod"))
    {
        PrintDiagnostics("RENAME_PREVIEW_DIAGNOSTIC", payload.RootElement.GetProperty("diagnostics"));
        throw new InvalidOperationException("Rename preview was not supported by the loaded VSIX bridge.");
    }

    var item = payload.RootElement.GetProperty("items")[0];
    if (item.GetProperty("totalTextChangeCount").GetInt32() <= 0)
    {
        PrintDiagnostics("RENAME_PREVIEW_DIAGNOSTIC", payload.RootElement.GetProperty("diagnostics"));
        throw new InvalidOperationException("Rename preview returned no text changes for LcLine.Start.");
    }

    Console.WriteLine(
        "RENAME_PREVIEW query=LcLine.Start affectedDocs={0} totalChanges={1} returnedChanges={2} conflicts={3} partial={4}",
        item.GetProperty("affectedDocumentCount").GetInt32(),
        item.GetProperty("totalTextChangeCount").GetInt32(),
        item.GetProperty("returnedTextChangeCount").GetInt32(),
        item.GetProperty("hasConflicts").GetBoolean(),
        payload.RootElement.GetProperty("isPartial").GetBoolean());
    PrintDiagnostics("RENAME_PREVIEW_DIAGNOSTIC", payload.RootElement.GetProperty("diagnostics"));
}

var projectGraph = await CallAsync(client, "get_csharp_project_graph", profile.WithTarget(new Dictionary<string, object?>
{
    ["projectName"] = "SampleWorkspace.Core",
    ["maxProjects"] = 20,
    ["maxMetadataReferencesPerProject"] = 10,
    ["includeMetadataReferences"] = true,
}));
using (var payload = ParseToolPayload(projectGraph))
{
    var item = payload.RootElement.GetProperty("items")[0];
    Console.WriteLine(
        "PROJECT_GRAPH project=SampleWorkspace.Core nodes={0} edges={1}",
        item.GetProperty("nodes").GetArrayLength(),
        item.GetProperty("edges").GetArrayLength());
}

var debugStatus = await CallAsync(client, "get_debugger_status", profile.WithTarget(new Dictionary<string, object?>()));
using (var payload = ParseToolPayload(debugStatus))
{
    var item = payload.RootElement.GetProperty("items")[0];
    Console.WriteLine(
        "DEBUG_STATUS state={0} isDebugging={1} isPaused={2}",
        item.GetProperty("state").GetString(),
        item.GetProperty("isDebugging").GetBoolean(),
        item.GetProperty("isPaused").GetBoolean());
}

var callStack = await CallAsync(client, "get_debug_call_stack", profile.WithTarget(new Dictionary<string, object?>
{
    ["maxFrames"] = 5,
}));
using (var payload = ParseToolPayload(callStack))
{
    Console.WriteLine(
        "DEBUG_CALL_STACK count={0} partial={1} diagnostics={2}",
        payload.RootElement.GetProperty("items").GetArrayLength(),
        payload.RootElement.GetProperty("isPartial").GetBoolean(),
        payload.RootElement.GetProperty("diagnostics").GetArrayLength());
}

var debugThreads = await CallAsync(client, "list_debug_threads", profile.WithTarget(new Dictionary<string, object?>()));
using (var payload = ParseToolPayload(debugThreads))
{
    Console.WriteLine(
        "DEBUG_THREADS count={0} partial={1} diagnostics={2}",
        payload.RootElement.GetProperty("items").GetArrayLength(),
        payload.RootElement.GetProperty("isPartial").GetBoolean(),
        payload.RootElement.GetProperty("diagnostics").GetArrayLength());
}

var frameVariables = await CallAsync(client, "get_debug_stack_frame_variables", profile.WithTarget(new Dictionary<string, object?>
{
    ["maxChildren"] = 5,
    ["maxStringLength"] = 200,
}));
using (var payload = ParseToolPayload(frameVariables))
{
    Console.WriteLine(
        "DEBUG_VARIABLES count={0} partial={1} diagnostics={2}",
        payload.RootElement.GetProperty("items").GetArrayLength(),
        payload.RootElement.GetProperty("isPartial").GetBoolean(),
        payload.RootElement.GetProperty("diagnostics").GetArrayLength());
}

var expression = await CallAsync(client, "evaluate_debug_expression", profile.WithTarget(new Dictionary<string, object?>
{
    ["expression"] = "this",
    ["maxChildren"] = 5,
    ["maxStringLength"] = 200,
    ["allowSideEffects"] = false,
}));
using (var payload = ParseToolPayload(expression))
{
    Console.WriteLine(
        "DEBUG_EXPRESSION count={0} partial={1} diagnostics={2}",
        payload.RootElement.GetProperty("items").GetArrayLength(),
        payload.RootElement.GetProperty("isPartial").GetBoolean(),
        payload.RootElement.GetProperty("diagnostics").GetArrayLength());
}

var breakpoints = await CallAsync(client, "list_debug_breakpoints", profile.WithTarget(new Dictionary<string, object?>
{
    ["maxResults"] = 20,
}));
using (var payload = ParseToolPayload(breakpoints))
{
    Console.WriteLine(
        "DEBUG_BREAKPOINTS count={0} partial={1}",
        payload.RootElement.GetProperty("items").GetArrayLength(),
        payload.RootElement.GetProperty("isPartial").GetBoolean());
}

var generatedDocuments = await CallAsync(client, "list_csharp_generated_documents", profile.WithTarget(new Dictionary<string, object?>
{
    ["projectName"] = "SampleWorkspace.LocalSolution",
    ["maxResults"] = 20,
    ["includeSourceGeneratedDocuments"] = true,
}));
using (var payload = ParseToolPayload(generatedDocuments))
{
    Console.WriteLine(
        "GENERATED_DOCUMENTS project=SampleWorkspace.LocalSolution count={0} partial={1}",
        payload.RootElement.GetProperty("items").GetArrayLength(),
        payload.RootElement.GetProperty("isPartial").GetBoolean());
}

var temporaryMarkers = await CallAsync(client, "find_csharp_temporary_markers", profile.WithTarget(new Dictionary<string, object?>
{
    ["projectName"] = "SampleWorkspace.Core",
    ["maxResults"] = 20,
    ["includeGeneratedCode"] = false,
}));
using (var payload = ParseToolPayload(temporaryMarkers))
{
    Console.WriteLine(
        "TEMPORARY_MARKERS project=SampleWorkspace.Core count={0} partial={1} diagnostics={2}",
        payload.RootElement.GetProperty("items").GetArrayLength(),
        payload.RootElement.GetProperty("isPartial").GetBoolean(),
        payload.RootElement.GetProperty("diagnostics").GetArrayLength());
}

var enclosingContext = await CallAsync(client, "get_csharp_enclosing_context", profile.WithTarget(new Dictionary<string, object?>
{
    ["filePath"] = profile.DocumentPath,
    ["line"] = profile.ContextLine,
    ["column"] = profile.ContextColumn,
    ["includeGeneratedCode"] = false,
}));
using (var payload = ParseToolPayload(enclosingContext))
{
    var item = payload.RootElement.GetProperty("items")[0];
    Console.WriteLine(
        "ENCLOSING_CONTEXT file=LcLine.cs type={0} member={1} localFunction={2}",
        item.GetProperty("type").GetString(),
        item.GetProperty("member").GetString(),
        item.GetProperty("localFunction").GetString());
}

await RunLineEndpointPocAsync(client, profile, "LcLine.Start", "Start", "SampleWorkspace.Core.Elements.LcLine");
await RunLineEndpointPocAsync(client, profile, "LcLine.End", "End", "SampleWorkspace.Core.Elements.LcLine");
await RunImplementationsPocAsync(client, profile, "IElement3d", "IElement3d", string.Empty);
await RunOverridesPocAsync(client, profile, "LcElement.Clone", "Clone", "SampleWorkspace.Core.LcElement");

static async Task RunGenericSmokeAsync(McpClient client, TestProfile profile)
{
    var instances = await CallAsync(client, "list_visual_studio_instances", new Dictionary<string, object?>
    {
        ["includeStale"] = true,
    });
    using (var payload = ParseToolPayload(instances))
    {
        Console.WriteLine(
            "GENERIC_INSTANCES count={0} partial={1}",
            payload.RootElement.GetProperty("items").GetArrayLength(),
            payload.RootElement.GetProperty("isPartial").GetBoolean());
    }

    var status = await CallAsync(
        client,
        "get_csharp_workspace_status",
        profile.WithTarget(new Dictionary<string, object?>()));
    using (var payload = ParseToolPayload(status))
    {
        var items = payload.RootElement.GetProperty("items");
        if (items.GetArrayLength() == 0)
        {
            PrintDiagnostics("GENERIC_STATUS_DIAGNOSTIC", payload.RootElement.GetProperty("diagnostics"));
            throw new InvalidOperationException(
                "Generic profile workspace status returned no items. Open the configured solution in Visual Studio and retry: "
                + profile.SolutionPath);
        }

        var item = items[0];
        Console.WriteLine(
            "GENERIC_STATUS profile={0} solutionPath={1} projects={2} documents={3} partial={4}",
            profile.Name,
            item.GetProperty("solutionPath").GetString(),
            item.GetProperty("projectCount").GetInt32(),
            item.GetProperty("documentCount").GetInt32(),
            payload.RootElement.GetProperty("isPartial").GetBoolean());
    }

    var search = await CallAsync(
        client,
        "search_csharp_symbols",
        profile.WithTarget(new Dictionary<string, object?>
        {
            ["queryText"] = profile.SymbolQuery,
            ["maxResults"] = 20,
            ["includeGeneratedCode"] = false,
        }));

    string symbolKey;
    using (var payload = ParseToolPayload(search))
    {
        var item = FindSymbol(
            payload.RootElement.GetProperty("items"),
            profile.ExpectedSymbolName,
            profile.ExpectedContainingType);
        symbolKey = item.GetProperty("key").GetProperty("value").GetString()
            ?? throw new InvalidOperationException("Generic profile search result did not include a symbol key.");
        Console.WriteLine(
            "GENERIC_SEARCH query={0} symbol={1}.{2} file={3} line={4}",
            profile.SymbolQuery,
            item.GetProperty("containingType").GetString(),
            item.GetProperty("name").GetString(),
            item.GetProperty("span").GetProperty("filePath").GetString(),
            item.GetProperty("span").GetProperty("startLine").GetInt32());
    }

    var definitions = await CallAsync(
        client,
        "find_csharp_definitions",
        profile.WithTarget(new Dictionary<string, object?>
        {
            ["symbolKey"] = symbolKey,
            ["includeGeneratedCode"] = false,
        }));
    using (var payload = ParseToolPayload(definitions))
    {
        Console.WriteLine("GENERIC_DEFINITIONS count={0}", payload.RootElement.GetProperty("items").GetArrayLength());
    }

    var references = await CallAsync(
        client,
        "find_csharp_references",
        profile.WithTarget(new Dictionary<string, object?>
        {
            ["symbolKey"] = symbolKey,
            ["includeGeneratedCode"] = false,
        }));
    using (var payload = ParseToolPayload(references))
    {
        Console.WriteLine(
            "GENERIC_REFERENCES count={0} partial={1}",
            payload.RootElement.GetProperty("items").GetArrayLength(),
            payload.RootElement.GetProperty("isPartial").GetBoolean());
    }

    var recursiveCallers = await CallAsync(
        client,
        "find_csharp_callers",
        profile.WithTarget(new Dictionary<string, object?>
        {
            ["symbolKey"] = symbolKey,
            ["maxDepth"] = 2,
            ["maxResults"] = 20,
            ["includeGeneratedCode"] = false,
        }));
    using (var payload = ParseToolPayload(recursiveCallers))
    {
        var items = payload.RootElement.GetProperty("items");
        var maxDepth = MaxCallGraphDepth(items);
        Console.WriteLine(
            "GENERIC_CALLERS_RECURSIVE count={0} maxDepth={1} partial={2}",
            items.GetArrayLength(),
            maxDepth,
            payload.RootElement.GetProperty("isPartial").GetBoolean());
        if (profile.EnforcesGenericSampleExpectations && maxDepth < 2)
        {
            throw new InvalidOperationException("Generic recursive callers should include at least one depth=2 edge.");
        }
    }

    var recursiveImpact = await CallAsync(
        client,
        "analyze_csharp_symbol_impact",
        profile.WithTarget(new Dictionary<string, object?>
        {
            ["symbolKey"] = symbolKey,
            ["maxDepth"] = 2,
            ["maxResults"] = 20,
            ["maxProjects"] = 10,
            ["maxFiles"] = 10,
            ["maxContainingTypes"] = 10,
            ["includeGeneratedCode"] = false,
        }));
    using (var payload = ParseToolPayload(recursiveImpact))
    {
        var item = payload.RootElement.GetProperty("items")[0];
        Console.WriteLine(
            "GENERIC_IMPACT_RECURSIVE total={0} direct={1} transitive={2} maxDepth={3} partial={4}",
            item.GetProperty("totalReferences").GetInt32(),
            item.GetProperty("directReferenceCount").GetInt32(),
            item.GetProperty("transitiveReferenceCount").GetInt32(),
            item.GetProperty("maxDepth").GetInt32(),
            payload.RootElement.GetProperty("isPartial").GetBoolean());
        if (profile.EnforcesGenericSampleExpectations && item.GetProperty("transitiveReferenceCount").GetInt32() < 1)
        {
            throw new InvalidOperationException("Generic recursive impact should include at least one transitive reference.");
        }
    }

    if (!string.IsNullOrWhiteSpace(profile.ProjectName))
    {
        var projectGraph = await CallAsync(
            client,
            "get_csharp_project_graph",
            profile.WithTarget(new Dictionary<string, object?>
            {
                ["projectName"] = profile.ProjectName,
                ["maxProjects"] = 20,
                ["maxMetadataReferencesPerProject"] = 10,
                ["includeMetadataReferences"] = true,
            }));
        using var payload = ParseToolPayload(projectGraph);
        var graph = payload.RootElement.GetProperty("items")[0];
        Console.WriteLine(
            "GENERIC_PROJECT_GRAPH project={0} nodes={1} edges={2}",
            profile.ProjectName,
            graph.GetProperty("nodes").GetArrayLength(),
            graph.GetProperty("edges").GetArrayLength());
    }

    if (!string.IsNullOrWhiteSpace(profile.ProjectName))
    {
        var generatedDocuments = await CallAsync(
            client,
            "list_csharp_generated_documents",
            profile.WithTarget(new Dictionary<string, object?>
            {
                ["projectName"] = profile.ProjectName,
                ["maxResults"] = 50,
                ["includeSourceGeneratedDocuments"] = true,
            }));
        using var payload = ParseToolPayload(generatedDocuments);
        var generatedItems = payload.RootElement.GetProperty("items");
        var sourceGeneratedCount = generatedItems
            .EnumerateArray()
            .Count(item => item.GetProperty("isSourceGenerated").GetBoolean());
        var generatedGreeterPath = generatedItems
            .EnumerateArray()
            .Where(item => item.GetProperty("isSourceGenerated").GetBoolean())
            .Select(item => item.GetProperty("filePath").GetString())
            .FirstOrDefault(path => path?.EndsWith("GeneratedGreeter.g.cs", StringComparison.OrdinalIgnoreCase) == true);
        Console.WriteLine(
            "GENERIC_GENERATED_DOCUMENTS project={0} count={1} sourceGenerated={2} partial={3}",
            profile.ProjectName,
            generatedItems.GetArrayLength(),
            sourceGeneratedCount,
            payload.RootElement.GetProperty("isPartial").GetBoolean());

        if (profile.EnforcesGenericSampleExpectations)
        {
            if (string.IsNullOrWhiteSpace(generatedGreeterPath))
            {
                throw new InvalidOperationException("Generic sample should expose GeneratedGreeter.g.cs as a source-generated document.");
            }

            var generatedSymbols = await CallAsync(
                client,
                "list_csharp_document_symbols",
                profile.WithTarget(new Dictionary<string, object?>
                {
                    ["filePath"] = generatedGreeterPath,
                    ["maxResults"] = 50,
                    ["includeGeneratedCode"] = true,
                }));
            using (var generatedSymbolsPayload = ParseToolPayload(generatedSymbols))
            {
                var items = generatedSymbolsPayload.RootElement.GetProperty("items");
                if (!ContainsDocumentSymbol(items, "GeneratedGreeter"))
                {
                    PrintDiagnostics("GENERIC_GENERATED_SYMBOLS_DIAGNOSTIC", generatedSymbolsPayload.RootElement.GetProperty("diagnostics"));
                    throw new InvalidOperationException("Source-generated document navigation did not return GeneratedGreeter.");
                }

                Console.WriteLine(
                    "GENERIC_GENERATED_SYMBOLS file=GeneratedGreeter.g.cs count={0} partial={1}",
                    items.GetArrayLength(),
                    generatedSymbolsPayload.RootElement.GetProperty("isPartial").GetBoolean());
            }

            var generatedSearch = await CallAsync(
                client,
                "search_csharp_symbols",
                profile.WithTarget(new Dictionary<string, object?>
                {
                    ["queryText"] = "GeneratedGreeter",
                    ["maxResults"] = 20,
                    ["includeGeneratedCode"] = true,
                }));
            using (var generatedSearchPayload = ParseToolPayload(generatedSearch))
            {
                _ = FindSymbol(generatedSearchPayload.RootElement.GetProperty("items"), "GeneratedGreeter", string.Empty);
                Console.WriteLine(
                    "GENERIC_GENERATED_SEARCH query=GeneratedGreeter count={0} partial={1}",
                    generatedSearchPayload.RootElement.GetProperty("items").GetArrayLength(),
                    generatedSearchPayload.RootElement.GetProperty("isPartial").GetBoolean());
            }
        }
    }

    if (!string.IsNullOrWhiteSpace(profile.DocumentPath))
    {
        var documentSymbols = await CallAsync(
            client,
            "list_csharp_document_symbols",
            profile.WithTarget(new Dictionary<string, object?>
            {
                ["filePath"] = profile.DocumentPath,
                ["maxResults"] = 200,
                ["includeGeneratedCode"] = false,
            }));
        using var payload = ParseToolPayload(documentSymbols);
        Console.WriteLine(
            "GENERIC_DOC_SYMBOLS count={0} partial={1}",
            payload.RootElement.GetProperty("items").GetArrayLength(),
            payload.RootElement.GetProperty("isPartial").GetBoolean());
    }

    if (!string.IsNullOrWhiteSpace(profile.DocumentPath) && profile.ContextLine > 0 && profile.ContextColumn > 0)
    {
        var enclosingContext = await CallAsync(
            client,
            "get_csharp_enclosing_context",
            profile.WithTarget(new Dictionary<string, object?>
            {
                ["filePath"] = profile.DocumentPath,
                ["line"] = profile.ContextLine,
                ["column"] = profile.ContextColumn,
                ["includeGeneratedCode"] = false,
            }));
        using var payload = ParseToolPayload(enclosingContext);
        var item = payload.RootElement.GetProperty("items")[0];
        Console.WriteLine(
            "GENERIC_ENCLOSING_CONTEXT type={0} member={1}",
            item.GetProperty("type").GetString(),
            item.GetProperty("member").GetString());
    }

    var debugStatus = await CallAsync(
        client,
        "get_debugger_status",
        profile.WithTarget(new Dictionary<string, object?>()));
    using (var payload = ParseToolPayload(debugStatus))
    {
        var item = payload.RootElement.GetProperty("items")[0];
        Console.WriteLine(
            "GENERIC_DEBUG_STATUS state={0} isDebugging={1} isPaused={2}",
            item.GetProperty("state").GetString(),
            item.GetProperty("isDebugging").GetBoolean(),
            item.GetProperty("isPaused").GetBoolean());
    }

    Console.WriteLine("GENERIC_RESULT profile={0} symbolKey={1}", profile.Name, symbolKey);
}

static async Task RunDebugControlSmokeAsync(McpClient client, TestProfile profile)
{
    var breakpointFile = profile.DocumentPath;

    await TryRemoveBreakpointAsync(client, profile, breakpointFile, line: 15);
    await TryRemoveBreakpointAsync(client, profile, breakpointFile, line: 29);
    await TryStopDebuggingAsync(client, profile);

    try
    {
        await RunPlainBreakpointScenarioAsync(client, profile, breakpointFile);
        await TryStopDebuggingAsync(client, profile);
        await TryRemoveBreakpointAsync(client, profile, breakpointFile, line: 29);

        await RunHitCountBreakpointScenarioAsync(client, profile, breakpointFile);
        await TryStopDebuggingAsync(client, profile);
        await TryRemoveBreakpointAsync(client, profile, breakpointFile, line: 29);

        await RunConditionalBreakpointScenarioAsync(client, profile, breakpointFile);
    }
    finally
    {
        await TryStopDebuggingAsync(client, profile);
        await TryRemoveBreakpointAsync(client, profile, breakpointFile, line: 15);
        await TryRemoveBreakpointAsync(client, profile, breakpointFile, line: 29);
    }

    Console.WriteLine("DEBUG_CONTROL_RESULT profile={0}", profile.Name);
}

static async Task RunPlainBreakpointScenarioAsync(McpClient client, TestProfile profile, string breakpointFile)
{
    var plainBreakpoint = await CallAsync(
        client,
        "set_debug_breakpoint",
        profile.WithTarget(new Dictionary<string, object?>
        {
            ["filePath"] = breakpointFile,
            ["line"] = 29,
            ["column"] = 1,
            ["timeoutMilliseconds"] = 0,
        }));
    using (var payload = ParseToolPayload(plainBreakpoint))
    {
        var breakpoint = RequireDebugControlBreakpoint(payload, "plain breakpoint");
        Console.WriteLine(
            "DEBUG_CONTROL_PLAIN file={0} line={1}",
            breakpoint.GetProperty("span").GetProperty("filePath").GetString(),
            breakpoint.GetProperty("span").GetProperty("startLine").GetInt32());
    }

    await RequireDebugControlSuccessAsync(client, profile, "start_debugging", "start debugging for plain breakpoint");
    await WaitForBreakAtLineAsync(client, profile, expectedLine: 29, scenario: "plain breakpoint");
}

static async Task RunHitCountBreakpointScenarioAsync(McpClient client, TestProfile profile, string breakpointFile)
{
    var hitCountBreakpoint = await CallAsync(
        client,
        "set_debug_breakpoint",
        profile.WithTarget(new Dictionary<string, object?>
        {
            ["filePath"] = breakpointFile,
            ["line"] = 29,
            ["column"] = 1,
            ["hitCountTarget"] = 3,
            ["hitCountMode"] = "Equal",
            ["timeoutMilliseconds"] = 0,
        }));
    using (var payload = ParseToolPayload(hitCountBreakpoint))
    {
        var breakpoint = RequireDebugControlBreakpoint(payload, "hit-count breakpoint");
        RequireJsonInt(breakpoint, "hitCountTarget", 3);
        RequireJsonString(breakpoint, "hitCountMode", "Equal");
        if (!breakpoint.TryGetProperty("currentHitCount", out _))
        {
            throw new InvalidOperationException("Hit-count breakpoint result did not include currentHitCount.");
        }

        Console.WriteLine(
            "DEBUG_CONTROL_HITCOUNT file={0} line={1} target={2} mode={3} current={4}",
            breakpoint.GetProperty("span").GetProperty("filePath").GetString(),
            breakpoint.GetProperty("span").GetProperty("startLine").GetInt32(),
            breakpoint.GetProperty("hitCountTarget").GetInt32(),
            breakpoint.GetProperty("hitCountMode").GetString(),
            breakpoint.GetProperty("currentHitCount").GetInt32());
    }

    await RequireBreakpointListFieldAsync(
        client,
        profile,
        item => item.TryGetProperty("hitCountTarget", out var hitCountTarget)
            && hitCountTarget.GetInt32() == 3
            && item.TryGetProperty("hitCountMode", out var hitCountMode)
            && string.Equals(hitCountMode.GetString(), "Equal", StringComparison.Ordinal)
            && item.TryGetProperty("currentHitCount", out _),
        "hit-count breakpoint");
    await RequireDebugControlSuccessAsync(client, profile, "start_debugging", "start debugging for hit-count breakpoint");
    await WaitForBreakAtLineAsync(client, profile, expectedLine: 29, scenario: "hit-count breakpoint");
    await RequireStackVariableValueAsync(client, profile, variableName: "left", expectedValue: "2", scenario: "hit-count breakpoint");
}

static async Task RunConditionalBreakpointScenarioAsync(McpClient client, TestProfile profile, string breakpointFile)
{
    var conditionalBreakpoint = await CallAsync(
        client,
        "set_debug_breakpoint",
        profile.WithTarget(new Dictionary<string, object?>
        {
            ["filePath"] = breakpointFile,
            ["line"] = 15,
            ["column"] = 1,
            ["condition"] = "i == 3",
            ["conditionMode"] = "WhenTrue",
            ["timeoutMilliseconds"] = 0,
        }));
    using (var payload = ParseToolPayload(conditionalBreakpoint))
    {
        var breakpoint = RequireDebugControlBreakpoint(payload, "conditional breakpoint");
        RequireJsonString(breakpoint, "condition", "i == 3");
        RequireJsonString(breakpoint, "conditionMode", "WhenTrue");
        Console.WriteLine(
            "DEBUG_CONTROL_CONDITION file={0} line={1} condition={2} mode={3}",
            breakpoint.GetProperty("span").GetProperty("filePath").GetString(),
            breakpoint.GetProperty("span").GetProperty("startLine").GetInt32(),
            breakpoint.GetProperty("condition").GetString(),
            breakpoint.GetProperty("conditionMode").GetString());
    }

    await RequireBreakpointListFieldAsync(
        client,
        profile,
        item => item.TryGetProperty("condition", out var condition)
            && string.Equals(condition.GetString(), "i == 3", StringComparison.Ordinal)
            && item.TryGetProperty("conditionMode", out var conditionMode)
            && string.Equals(conditionMode.GetString(), "WhenTrue", StringComparison.Ordinal),
        "conditional breakpoint");
    await RequireDebugControlSuccessAsync(client, profile, "start_debugging", "start debugging for conditional breakpoint");
    await WaitForBreakAtLineAsync(client, profile, expectedLine: 15, scenario: "conditional breakpoint");
    await RequireStackVariableValueAsync(client, profile, variableName: "i", expectedValue: "3", scenario: "conditional breakpoint");
}

static async Task TryRemoveBreakpointAsync(McpClient client, TestProfile profile, string filePath, int line)
{
    try
    {
        _ = await CallAsync(
            client,
            "remove_debug_breakpoint",
            profile.WithTarget(new Dictionary<string, object?>
            {
                ["filePath"] = filePath,
                ["line"] = line,
                ["timeoutMilliseconds"] = 0,
            }));
    }
    catch
    {
    }
}

static async Task TryStopDebuggingAsync(McpClient client, TestProfile profile)
{
    try
    {
        _ = await CallAsync(
            client,
            "stop_debugging",
            profile.WithTarget(new Dictionary<string, object?>
            {
                ["timeoutMilliseconds"] = 5000,
            }));
    }
    catch
    {
    }
}

static async Task RequireDebugControlSuccessAsync(
    McpClient client,
    TestProfile profile,
    string toolName,
    string scenario)
{
    var result = await CallAsync(
        client,
        toolName,
        profile.WithTarget(new Dictionary<string, object?>
        {
            ["timeoutMilliseconds"] = 0,
        }));
    using var payload = ParseToolPayload(result);
    var item = payload.RootElement.GetProperty("items")[0];
    if (!item.GetProperty("succeeded").GetBoolean())
    {
        throw new InvalidOperationException($"{scenario} did not succeed: {payload.RootElement}");
    }

    Console.WriteLine(
        "DEBUG_CONTROL_ACTION tool={0} state={1}",
        toolName,
        item.GetProperty("status").GetProperty("state").GetString());
}

static async Task WaitForBreakAtLineAsync(
    McpClient client,
    TestProfile profile,
    int expectedLine,
    string scenario)
{
    string? lastStatusJson = null;
    for (var attempt = 0; attempt < 80; attempt++)
    {
        var status = await CallAsync(
            client,
            "get_debugger_status",
            profile.WithTarget(new Dictionary<string, object?>()));
        using var payload = ParseToolPayload(status);
        var items = payload.RootElement.GetProperty("items");
        if (items.GetArrayLength() == 0)
        {
            lastStatusJson = payload.RootElement.GetRawText();
            await Task.Delay(250);
            continue;
        }

        var item = items[0];
        var state = item.GetProperty("state").GetString();
        var line = TryGetCurrentFrameLine(item) ?? TryGetCurrentBreakpointLine(item);
        lastStatusJson = item.GetRawText();
        if (string.Equals(state, "Break", StringComparison.Ordinal) && line == expectedLine)
        {
            Console.WriteLine(
                "DEBUG_CONTROL_BREAK scenario={0} line={1}",
                scenario,
                expectedLine);
            return;
        }

        if (string.Equals(state, "Break", StringComparison.Ordinal) && line is not null)
        {
            throw new InvalidOperationException($"{scenario} stopped at unexpected line {line}; expected line {expectedLine}.");
        }

        await Task.Delay(250);
    }

    var breakpointsJson = await TryGetBreakpointSnapshotAsync(client, profile);
    throw new InvalidOperationException(
        $"{scenario} did not break at line {expectedLine} within the expected time. Last status: {lastStatusJson ?? "<none>"}. Breakpoints: {breakpointsJson}");
}

static async Task RequireBreakpointListFieldAsync(
    McpClient client,
    TestProfile profile,
    Func<JsonElement, bool> predicate,
    string scenario)
{
    var breakpoints = await CallAsync(
        client,
        "list_debug_breakpoints",
        profile.WithTarget(new Dictionary<string, object?>
        {
            ["maxResults"] = 50,
        }));
    using var payload = ParseToolPayload(breakpoints);
    var items = payload.RootElement.GetProperty("items");
    foreach (var item in items.EnumerateArray())
    {
        if (predicate(item))
        {
            Console.WriteLine(
                "DEBUG_CONTROL_BREAKPOINTS scenario={0} count={1}",
                scenario,
                items.GetArrayLength());
            return;
        }
    }

    throw new InvalidOperationException($"list_debug_breakpoints did not return the expected fields for {scenario}: {payload.RootElement}");
}

static async Task RequireStackVariableValueAsync(
    McpClient client,
    TestProfile profile,
    string variableName,
    string expectedValue,
    string scenario)
{
    var variables = await CallAsync(
        client,
        "get_debug_stack_frame_variables",
        profile.WithTarget(new Dictionary<string, object?>
        {
            ["maxChildren"] = 0,
            ["maxStringLength"] = 200,
        }));
    using var payload = ParseToolPayload(variables);
    foreach (var item in payload.RootElement.GetProperty("items").EnumerateArray())
    {
        if (string.Equals(item.GetProperty("name").GetString(), variableName, StringComparison.Ordinal)
            && string.Equals(item.GetProperty("value").GetString(), expectedValue, StringComparison.Ordinal))
        {
            Console.WriteLine(
                "DEBUG_CONTROL_VARIABLE scenario={0} {1}={2}",
                scenario,
                variableName,
                expectedValue);
            return;
        }
    }

    throw new InvalidOperationException(
        $"{scenario} did not expose expected variable {variableName}={expectedValue}: {payload.RootElement}");
}

static async Task<string> TryGetBreakpointSnapshotAsync(McpClient client, TestProfile profile)
{
    try
    {
        var breakpoints = await CallAsync(
            client,
            "list_debug_breakpoints",
            profile.WithTarget(new Dictionary<string, object?>
            {
                ["maxResults"] = 50,
            }));
        using var payload = ParseToolPayload(breakpoints);
        return payload.RootElement.GetRawText();
    }
    catch (Exception ex)
    {
        return "BreakpointSnapshotFailed: " + ex.Message;
    }
}

static int? TryGetCurrentFrameLine(JsonElement debugStatus)
{
    if (!debugStatus.TryGetProperty("currentFrame", out var frame)
        || frame.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined
        || !frame.TryGetProperty("span", out var span)
        || span.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined
        || !span.TryGetProperty("startLine", out var line))
    {
        return null;
    }

    return line.GetInt32();
}

static int? TryGetCurrentBreakpointLine(JsonElement debugStatus)
{
    if (!debugStatus.TryGetProperty("currentBreakpoint", out var breakpoint)
        || breakpoint.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined
        || !breakpoint.TryGetProperty("span", out var span)
        || span.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined
        || !span.TryGetProperty("startLine", out var line))
    {
        return null;
    }

    return line.GetInt32();
}

static JsonElement RequireDebugControlBreakpoint(JsonDocument payload, string scenario)
{
    var item = payload.RootElement.GetProperty("items")[0];
    if (!item.GetProperty("succeeded").GetBoolean())
    {
        throw new InvalidOperationException($"{scenario} did not succeed: {payload.RootElement}");
    }

    if (!item.TryGetProperty("breakpoint", out var breakpoint)
        || breakpoint.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
    {
        throw new InvalidOperationException($"{scenario} did not return a breakpoint descriptor: {payload.RootElement}");
    }

    return breakpoint.Clone();
}

static void RequireJsonString(JsonElement item, string propertyName, string expected)
{
    if (!item.TryGetProperty(propertyName, out var property)
        || !string.Equals(property.GetString(), expected, StringComparison.Ordinal))
    {
        throw new InvalidOperationException($"Expected {propertyName}={expected}, actual item: {item}");
    }
}

static void RequireJsonInt(JsonElement item, string propertyName, int expected)
{
    if (!item.TryGetProperty(propertyName, out var property)
        || property.GetInt32() != expected)
    {
        throw new InvalidOperationException($"Expected {propertyName}={expected}, actual item: {item}");
    }
}

static async Task<string> CallAsync(
    McpClient client,
    string toolName,
    IReadOnlyDictionary<string, object?> arguments)
{
    var result = await client.CallToolAsync(toolName, arguments!);
    if (result.IsError == true)
    {
        throw new InvalidOperationException($"{toolName} returned MCP error: {Serialize(result)}");
    }

    return Serialize(result);
}

static string ExtractFirstSymbolKey(string resultJson)
{
    using var payload = ParseToolPayload(resultJson);
    var key = payload.RootElement.GetProperty("items")[0].GetProperty("key").GetProperty("value").GetString();
    if (string.IsNullOrWhiteSpace(key))
    {
        throw new InvalidOperationException("Search result did not include a symbol key.");
    }

    return key;
}

static void RunToolSchemaSmoke(
    IReadOnlyCollection<string> toolNames,
    IList<McpClientTool> tools,
    IList<McpClientResourceTemplate> resourceTemplates)
{
    var configuredExpectedTools = Environment.GetEnvironmentVariable("CODE_NAVIGATOR_EXPECTED_TOOLS");
    var expectedTools = string.IsNullOrWhiteSpace(configuredExpectedTools)
        ? new[]
        {
            "analyze_csharp_build_errors",
            "analyze_csharp_repo_workflow",
            "apply_csharp_cleanup",
            "apply_csharp_code_fix",
            "apply_csharp_fix_all",
            "apply_csharp_rename",
            "batch_get_csharp_source_contexts",
            "batch_get_csharp_symbol_sources",
            "check_visual_studio_csharp_navigator_health",
            "collect_artifact_evidence",
            "find_csharp_solutions",
            "generate_csharp_agent_instructions",
            "get_csharp_source_context",
            "get_csharp_symbol_source",
            "get_csharp_task_context",
            "get_csharp_workflow_performance_snapshot",
            "get_visual_studio_active_document_context",
            "get_visual_studio_error_list",
            "get_visual_studio_open_documents",
            "get_visual_studio_output_window",
            "investigate_csharp_build_failure",
            "investigate_csharp_runtime_exception",
            "list_csharp_code_fixes",
            "merge_csharp_agent_findings",
            "open_csharp_source_location",
            "open_csharp_solution_in_visual_studio",
            "plan_csharp_verification",
            "plan_csharp_regression_scope",
            "prepare_csharp_change_review",
            "plan_csharp_debug_scenario",
            "prepare_csharp_edit_task",
            "prepare_csharp_verification_run",
            "prepare_debug_session",
            "preview_csharp_cleanup",
            "preview_csharp_code_fix",
            "preview_csharp_fix_all",
            "preview_csharp_refactoring_plan",
            "prepare_csharp_workspace",
            "split_csharp_agent_work",
            "start_csharp_investigation",
            "wait_for_artifact_evidence",
            "wait_for_visual_studio_bridge",
        }
        : configuredExpectedTools.Split(
            new[] { ',', ';' },
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    var missingTools = expectedTools
        .Where(expectedTool => !toolNames.Contains(expectedTool, StringComparer.Ordinal))
        .ToArray();
    if (missingTools.Length > 0)
    {
        throw new InvalidOperationException("Missing expected MCP tools: " + string.Join(", ", missingTools));
    }

    Console.WriteLine(
        "TOOL_SCHEMA total={0} expected={1}",
        toolNames.Count,
        string.Join(",", expectedTools.OrderBy(name => name)));

    var snapshotPath = Environment.GetEnvironmentVariable("CODE_NAVIGATOR_SCHEMA_SNAPSHOT_PATH");
    if (!string.IsNullOrWhiteSpace(snapshotPath))
    {
        WriteToolSchemaSnapshot(snapshotPath, tools, resourceTemplates);
        Console.WriteLine("TOOL_SCHEMA_SNAPSHOT path={0}", Path.GetFullPath(snapshotPath));
    }
}

static void WriteToolSchemaSnapshot(
    string snapshotPath,
    IList<McpClientTool> tools,
    IList<McpClientResourceTemplate> resourceTemplates)
{
    var fullPath = Path.GetFullPath(snapshotPath);
    Directory.CreateDirectory(Path.GetDirectoryName(fullPath)
        ?? throw new InvalidOperationException("Snapshot path has no parent directory."));

    var snapshot = new
    {
        generatedAtUtc = DateTimeOffset.UtcNow,
        toolCount = tools.Count,
        resourceTemplateCount = resourceTemplates.Count,
        tools = tools
            .OrderBy(tool => tool.Name, StringComparer.Ordinal)
            .Select(tool => tool.ProtocolTool)
            .ToArray(),
        resourceTemplates = resourceTemplates
            .OrderBy(template => template.UriTemplate, StringComparer.Ordinal)
            .Select(template => template.ProtocolResourceTemplate)
            .ToArray(),
    };

    File.WriteAllText(
        fullPath,
        JsonSerializer.Serialize(snapshot, new JsonSerializerOptions
        {
            WriteIndented = true,
        }));
}

static async Task RunAgenticResourcesSmokeAsync(McpClient client)
{
    var result = await CallAsync(client, "prepare_csharp_edit_task", new Dictionary<string, object?>
    {
        ["problemText"] = "Prepare an edit task without an active Visual Studio bridge.",
        ["symbolQuery"] = "Widget",
        ["maxSourceSnippets"] = 0,
    });

    string? contextUri;
    using (var payload = ParseToolPayload(result))
    {
        var item = payload.RootElement.GetProperty("items")[0];
        var packet = item.GetProperty("evidencePacket");
        var links = packet.GetProperty("resourceLinks");
        contextUri = links.EnumerateArray()
            .FirstOrDefault(link => string.Equals(link.GetProperty("kind").GetString(), "context", StringComparison.Ordinal))
            .GetProperty("uri")
            .GetString();
    }

    if (string.IsNullOrWhiteSpace(contextUri))
    {
        throw new InvalidOperationException("prepare_csharp_edit_task did not return a context resource link.");
    }

    var resources = await client.ListResourcesAsync();
    if (!resources.Any(resource => string.Equals(resource.Uri, contextUri, StringComparison.Ordinal)))
    {
        throw new InvalidOperationException("resources/list did not expose the generated context resource.");
    }

    var read = await client.ReadResourceAsync(contextUri, cancellationToken: CancellationToken.None);
    var text = read.Contents.OfType<TextResourceContents>().FirstOrDefault()?.Text;
    if (string.IsNullOrWhiteSpace(text) || !text.Contains("Prepare an edit task", StringComparison.Ordinal))
    {
        throw new InvalidOperationException("resources/read did not return the generated task context text.");
    }

    Console.WriteLine("AGENTIC_RESOURCES uri={0} textLength={1}", contextUri, text.Length);

    var reviewResult = await CallAsync(client, "prepare_csharp_change_review", new Dictionary<string, object?>
    {
        ["problemText"] = "Review a change without an active Visual Studio bridge.",
        ["changedFiles"] = new[] { "src/Feature/Widget.cs" },
        ["symbolQuery"] = "Widget",
    });

    string? reviewUri;
    using (var payload = ParseToolPayload(reviewResult))
    {
        var item = payload.RootElement.GetProperty("items")[0];
        var packet = item.GetProperty("evidencePacket");
        var links = packet.GetProperty("resourceLinks");
        reviewUri = links.EnumerateArray()
            .FirstOrDefault(link => string.Equals(link.GetProperty("kind").GetString(), "review", StringComparison.Ordinal))
            .GetProperty("uri")
            .GetString();
    }

    if (string.IsNullOrWhiteSpace(reviewUri))
    {
        throw new InvalidOperationException("prepare_csharp_change_review did not return a review resource link.");
    }

    resources = await client.ListResourcesAsync();
    if (!resources.Any(resource => string.Equals(resource.Uri, reviewUri, StringComparison.Ordinal)))
    {
        throw new InvalidOperationException("resources/list did not expose the generated review resource.");
    }

    read = await client.ReadResourceAsync(reviewUri, cancellationToken: CancellationToken.None);
    text = read.Contents.OfType<TextResourceContents>().FirstOrDefault()?.Text;
    if (string.IsNullOrWhiteSpace(text) || !text.Contains("Review a change", StringComparison.Ordinal))
    {
        throw new InvalidOperationException("resources/read did not return the generated change review text.");
    }

    Console.WriteLine("AGENTIC_REVIEW_RESOURCE uri={0} textLength={1}", reviewUri, text.Length);

    var verificationResult = await CallAsync(client, "prepare_csharp_verification_run", new Dictionary<string, object?>
    {
        ["problemText"] = "Plan verification without an active Visual Studio bridge.",
        ["changedFiles"] = new[] { "src/Feature/Widget.cs" },
        ["symbolQuery"] = "Widget",
    });

    string? verificationUri;
    using (var payload = ParseToolPayload(verificationResult))
    {
        var item = payload.RootElement.GetProperty("items")[0];
        var packet = item.GetProperty("evidencePacket");
        var links = packet.GetProperty("resourceLinks");
        verificationUri = links.EnumerateArray()
            .FirstOrDefault(link => string.Equals(link.GetProperty("kind").GetString(), "verification", StringComparison.Ordinal))
            .GetProperty("uri")
            .GetString();
    }

    if (string.IsNullOrWhiteSpace(verificationUri))
    {
        throw new InvalidOperationException("prepare_csharp_verification_run did not return a verification resource link.");
    }

    resources = await client.ListResourcesAsync();
    if (!resources.Any(resource => string.Equals(resource.Uri, verificationUri, StringComparison.Ordinal)))
    {
        throw new InvalidOperationException("resources/list did not expose the generated verification resource.");
    }

    read = await client.ReadResourceAsync(verificationUri, cancellationToken: CancellationToken.None);
    text = read.Contents.OfType<TextResourceContents>().FirstOrDefault()?.Text;
    if (string.IsNullOrWhiteSpace(text) || !text.Contains("Verification", StringComparison.Ordinal))
    {
        throw new InvalidOperationException("resources/read did not return the generated verification text.");
    }

    Console.WriteLine("AGENTIC_VERIFICATION_RESOURCE uri={0} textLength={1}", verificationUri, text.Length);

    var runtimeResult = await CallAsync(client, "investigate_csharp_runtime_exception", new Dictionary<string, object?>
    {
        ["problemText"] = "Investigate runtime exception without an active Visual Studio bridge.",
        ["exceptionText"] = "System.InvalidOperationException: sample failure",
        ["maxSourceSnippets"] = 0,
    });

    string? debugUri;
    using (var payload = ParseToolPayload(runtimeResult))
    {
        var item = payload.RootElement.GetProperty("items")[0];
        var packet = item.GetProperty("evidencePacket");
        var links = packet.GetProperty("resourceLinks");
        debugUri = links.EnumerateArray()
            .FirstOrDefault(link => string.Equals(link.GetProperty("kind").GetString(), "debug-evidence", StringComparison.Ordinal))
            .GetProperty("uri")
            .GetString();
    }

    if (string.IsNullOrWhiteSpace(debugUri))
    {
        throw new InvalidOperationException("investigate_csharp_runtime_exception did not return a debug-evidence resource link.");
    }

    resources = await client.ListResourcesAsync();
    if (!resources.Any(resource => string.Equals(resource.Uri, debugUri, StringComparison.Ordinal)))
    {
        throw new InvalidOperationException("resources/list did not expose the generated debug-evidence resource.");
    }

    read = await client.ReadResourceAsync(debugUri, cancellationToken: CancellationToken.None);
    text = read.Contents.OfType<TextResourceContents>().FirstOrDefault()?.Text;
    if (string.IsNullOrWhiteSpace(text) || !text.Contains("sample failure", StringComparison.Ordinal))
    {
        throw new InvalidOperationException("resources/read did not return the generated runtime debug evidence text.");
    }

    Console.WriteLine("AGENTIC_DEBUG_RESOURCE uri={0} textLength={1}", debugUri, text.Length);

    var buildFailureResult = await CallAsync(client, "investigate_csharp_build_failure", new Dictionary<string, object?>
    {
        ["problemText"] = "Investigate build failure without an active Visual Studio bridge.",
        ["buildOutput"] = "D:\\A\\src\\Feature\\Widget.cs(10,17): error CS0246: The type or namespace name 'WidgetState' could not be found [D:\\A\\Feature.Project.csproj]",
        ["changedFiles"] = new[] { "src/Feature/Widget.cs" },
        ["includeVisualStudioBuildOutput"] = false,
        ["maxErrorListItems"] = 0,
        ["maxSourceSnippets"] = 0,
    });

    string? buildFailureUri;
    string? buildLogUri;
    using (var payload = ParseToolPayload(buildFailureResult))
    {
        var item = payload.RootElement.GetProperty("items")[0];
        var packet = item.GetProperty("evidencePacket");
        var links = packet.GetProperty("resourceLinks");
        buildFailureUri = links.EnumerateArray()
            .FirstOrDefault(link => string.Equals(link.GetProperty("kind").GetString(), "build-failure", StringComparison.Ordinal))
            .GetProperty("uri")
            .GetString();
        buildLogUri = links.EnumerateArray()
            .FirstOrDefault(link => string.Equals(link.GetProperty("kind").GetString(), "build-log", StringComparison.Ordinal))
            .GetProperty("uri")
            .GetString();
    }

    if (string.IsNullOrWhiteSpace(buildFailureUri) || string.IsNullOrWhiteSpace(buildLogUri))
    {
        throw new InvalidOperationException("investigate_csharp_build_failure did not return build-failure and build-log resource links.");
    }

    resources = await client.ListResourcesAsync();
    if (!resources.Any(resource => string.Equals(resource.Uri, buildFailureUri, StringComparison.Ordinal)))
    {
        throw new InvalidOperationException("resources/list did not expose the generated build-failure resource.");
    }

    read = await client.ReadResourceAsync(buildFailureUri, cancellationToken: CancellationToken.None);
    text = read.Contents.OfType<TextResourceContents>().FirstOrDefault()?.Text;
    if (string.IsNullOrWhiteSpace(text) || !text.Contains("Root-cause candidates", StringComparison.Ordinal))
    {
        throw new InvalidOperationException("resources/read did not return the generated build failure evidence text.");
    }

    Console.WriteLine("AGENTIC_BUILD_FAILURE_RESOURCE uri={0} textLength={1}", buildFailureUri, text.Length);

    read = await client.ReadResourceAsync(buildLogUri, cancellationToken: CancellationToken.None);
    text = read.Contents.OfType<TextResourceContents>().FirstOrDefault()?.Text;
    if (string.IsNullOrWhiteSpace(text) || !text.Contains("CS0246", StringComparison.Ordinal))
    {
        throw new InvalidOperationException("resources/read did not return the generated build-log evidence text.");
    }

    Console.WriteLine("AGENTIC_BUILD_LOG_RESOURCE uri={0} textLength={1}", buildLogUri, text.Length);
}

static async Task RunOutputWindowSmokeAsync(McpClient client, TestProfile profile)
{
    var paneName = Environment.GetEnvironmentVariable("CODE_NAVIGATOR_TEST_OUTPUT_PANE");
    if (string.IsNullOrWhiteSpace(paneName))
    {
        paneName = "Build";
    }

    var outputWindow = await CallAsync(client, "get_visual_studio_output_window", profile.WithTarget(new Dictionary<string, object?>
    {
        ["paneName"] = paneName,
        ["maxCharacters"] = 2000,
    }));

    using var payload = ParseToolPayload(outputWindow);
    var items = payload.RootElement.GetProperty("items");
    if (items.GetArrayLength() == 0)
    {
        throw new InvalidOperationException("Output Window smoke returned no panes: " + payload.RootElement);
    }

    var item = items[0];
    Console.WriteLine(
        "OUTPUT_WINDOW requested={0} pane={1} returned={2} total={3} partial={4}",
        paneName,
        item.GetProperty("paneName").GetString(),
        item.GetProperty("returnedCharacters").GetInt32(),
        item.GetProperty("totalCharacters").GetInt32(),
        payload.RootElement.GetProperty("isPartial").GetBoolean());

    if (payload.RootElement.TryGetProperty("diagnostics", out var diagnostics)
        && diagnostics.ValueKind == JsonValueKind.Array
        && diagnostics.GetArrayLength() > 0)
    {
        Console.WriteLine(
            "OUTPUT_WINDOW_DIAGNOSTICS " +
            string.Join(" | ", diagnostics.EnumerateArray().Select(diagnostic => diagnostic.GetString())));
    }
}

static async Task RunCodeFixSmokeAsync(McpClient client, TestProfile profile)
{
    var singleDocument = profile.DocumentPath;
    var projectDirectory = Path.GetDirectoryName(singleDocument)
        ?? throw new InvalidOperationException("CodeFix profile document path has no directory.");
    var fixAllDocument = Path.Combine(projectDirectory, "CodeFixAllSample.cs");

    var singleBaselineSource = CreateCodeFixBaselineSource("CodeFixSingleSample", 42);
    var fixAllBaselineSource = CreateCodeFixBaselineSource("CodeFixAllSample", 84);
    var singleDiagnosticSource = CreateSingleCodeFixDiagnosticSource();
    var fixAllDiagnosticSource = CreateFixAllCodeFixDiagnosticSource();
    File.WriteAllText(singleDocument, singleDiagnosticSource);
    File.WriteAllText(fixAllDocument, fixAllDiagnosticSource);

    try
    {
        var singleCandidate = await WaitForCodeFixCandidateAsync(
            client,
            profile,
            scopeKind: "Document",
            filePath: singleDocument,
            projectName: null,
            diagnosticId: "CS0103",
            requireFixAll: false,
            scenario: "single code fix");

        var codeFixPreview = await CallAsync(client, "preview_csharp_code_fix", profile.WithTarget(new Dictionary<string, object?>
        {
            ["diagnosticId"] = singleCandidate.GetProperty("diagnosticId").GetString(),
            ["scopeKind"] = "Document",
            ["filePath"] = singleDocument,
            ["fixTitle"] = singleCandidate.GetProperty("title").GetString(),
            ["providerName"] = singleCandidate.GetProperty("providerName").GetString(),
            ["equivalenceKey"] = singleCandidate.GetProperty("equivalenceKey").GetString(),
            ["candidateStableKey"] = singleCandidate.GetProperty("stableKey").GetString(),
            ["maxTextChanges"] = 20,
            ["maxSnippetLength"] = 160,
        }));

        var codeFixSessionId = RequireMutationPreview(codeFixPreview, "CODEFIX_PREVIEW");

        var codeFixApply = await CallAsync(client, "apply_csharp_code_fix", profile.WithTarget(new Dictionary<string, object?>
        {
            ["diagnosticId"] = singleCandidate.GetProperty("diagnosticId").GetString(),
            ["scopeKind"] = "Document",
            ["filePath"] = singleDocument,
            ["previewSessionId"] = codeFixSessionId,
            ["maxTextChanges"] = 20,
            ["maxSnippetLength"] = 160,
        }));
        RequireMutationApply(codeFixApply, "CODEFIX_APPLY");

        var fixAllCandidate = await WaitForCodeFixCandidateAsync(
            client,
            profile,
            scopeKind: "Project",
            filePath: null,
            projectName: profile.ProjectName,
            diagnosticId: "CS0219",
            requireFixAll: true,
            scenario: "project fix all");

        var fixAllPreview = await CallAsync(client, "preview_csharp_fix_all", profile.WithTarget(new Dictionary<string, object?>
        {
            ["diagnosticId"] = fixAllCandidate.GetProperty("diagnosticId").GetString(),
            ["scopeKind"] = "Project",
            ["projectName"] = profile.ProjectName,
            ["fixTitle"] = fixAllCandidate.GetProperty("title").GetString(),
            ["providerName"] = fixAllCandidate.GetProperty("providerName").GetString(),
            ["equivalenceKey"] = fixAllCandidate.GetProperty("equivalenceKey").GetString(),
            ["candidateStableKey"] = fixAllCandidate.GetProperty("stableKey").GetString(),
            ["maxTextChanges"] = 40,
            ["maxSnippetLength"] = 160,
        }));

        var fixAllSessionId = RequireMutationPreview(fixAllPreview, "FIXALL_PREVIEW");

        var fixAllApply = await CallAsync(client, "apply_csharp_fix_all", profile.WithTarget(new Dictionary<string, object?>
        {
            ["diagnosticId"] = fixAllCandidate.GetProperty("diagnosticId").GetString(),
            ["scopeKind"] = "Project",
            ["projectName"] = profile.ProjectName,
            ["previewSessionId"] = fixAllSessionId,
            ["maxTextChanges"] = 40,
            ["maxSnippetLength"] = 160,
        }));
        RequireMutationApply(fixAllApply, "FIXALL_APPLY");
    }
    finally
    {
        File.WriteAllText(singleDocument, singleBaselineSource);
        File.WriteAllText(fixAllDocument, fixAllBaselineSource);
    }
}

static async Task<JsonElement> WaitForCodeFixCandidateAsync(
    McpClient client,
    TestProfile profile,
    string scopeKind,
    string? filePath,
    string? projectName,
    string diagnosticId,
    bool requireFixAll,
    string scenario)
{
    string? lastPayload = null;
    for (var attempt = 0; attempt < 20; attempt++)
    {
        var arguments = profile.WithTarget(new Dictionary<string, object?>
        {
            ["scopeKind"] = scopeKind,
            ["filePath"] = filePath,
            ["projectName"] = projectName,
            ["diagnosticId"] = diagnosticId,
            ["minimumSeverity"] = "Hidden",
            ["maxResults"] = 20,
        });

        var result = await CallAsync(client, "list_csharp_code_fixes", arguments);
        using var payload = ParseToolPayload(result);
        lastPayload = payload.RootElement.GetRawText();
        foreach (var candidate in payload.RootElement.GetProperty("items").EnumerateArray())
        {
            if (!string.Equals(candidate.GetProperty("diagnosticId").GetString(), diagnosticId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!candidate.GetProperty("supportsPreview").GetBoolean()
                || !candidate.GetProperty("supportsApply").GetBoolean())
            {
                continue;
            }

            if (requireFixAll && !candidate.GetProperty("supportsFixAll").GetBoolean())
            {
                continue;
            }

            Console.WriteLine(
                "CODEFIX_CANDIDATE scenario={0} title={1} provider={2} fixAll={3}",
                scenario,
                candidate.GetProperty("title").GetString(),
                candidate.GetProperty("providerName").GetString(),
                candidate.GetProperty("supportsFixAll").GetBoolean());
            return candidate.Clone();
        }

        await Task.Delay(500);
    }

    throw new InvalidOperationException($"No provider-backed {diagnosticId} candidate was found for {scenario}. Last payload: {lastPayload}");
}

static string RequireMutationPreview(string resultJson, string label)
{
    using var payload = ParseToolPayload(resultJson);
    var items = payload.RootElement.GetProperty("items");
    if (items.GetArrayLength() != 1)
    {
        throw new InvalidOperationException($"{label} expected exactly one preview item: {payload.RootElement}");
    }

    var preview = items[0].GetProperty("mutationPreview");
    var sessionId = preview.GetProperty("sessionId").GetString();
    if (string.IsNullOrWhiteSpace(sessionId))
    {
        throw new InvalidOperationException($"{label} did not return a mutation session id: {payload.RootElement}");
    }

    if (preview.GetProperty("totalTextChangeCount").GetInt32() < 1)
    {
        throw new InvalidOperationException($"{label} did not return text changes: {payload.RootElement}");
    }

    if (preview.GetProperty("blockers").GetArrayLength() > 0)
    {
        throw new InvalidOperationException($"{label} returned blockers: {payload.RootElement}");
    }

    Console.WriteLine(
        "{0} session={1} docs={2} changes={3}",
        label,
        sessionId,
        preview.GetProperty("affectedDocumentCount").GetInt32(),
        preview.GetProperty("totalTextChangeCount").GetInt32());
    return sessionId;
}

static void RequireMutationApply(string resultJson, string label)
{
    using var payload = ParseToolPayload(resultJson);
    var items = payload.RootElement.GetProperty("items");
    if (items.GetArrayLength() != 1)
    {
        throw new InvalidOperationException($"{label} expected exactly one apply item: {payload.RootElement}");
    }

    var item = items[0];
    if (!item.GetProperty("applied").GetBoolean())
    {
        throw new InvalidOperationException($"{label} did not apply: {payload.RootElement}");
    }

    var mutationResult = item.GetProperty("mutationResult");
    if (!mutationResult.GetProperty("applied").GetBoolean())
    {
        throw new InvalidOperationException($"{label} mutation result was not applied: {payload.RootElement}");
    }

    Console.WriteLine(
        "{0} session={1} changes={2}",
        label,
        mutationResult.GetProperty("sessionId").GetString(),
        mutationResult.GetProperty("preview").GetProperty("totalTextChangeCount").GetInt32());
}

static string CreateSingleCodeFixDiagnosticSource()
{
    return string.Join(
        "\r\n",
        "namespace CodeNavigator.Sample;",
        string.Empty,
        "public sealed class CodeFixSingleSample",
        "{",
        "    public int Value => MissingValue;",
        "}",
        string.Empty);
}

static string CreateFixAllCodeFixDiagnosticSource()
{
    return string.Join(
        "\r\n",
        "namespace CodeNavigator.Sample;",
        string.Empty,
        "public sealed class CodeFixAllSample",
        "{",
        "    public int Value()",
        "    {",
        "        var unused = 84;",
        "        return 84;",
        "    }",
        "}",
        string.Empty);
}

static string CreateCodeFixBaselineSource(string typeName, int value)
{
    return string.Join(
        "\r\n",
        "namespace CodeNavigator.Sample;",
        string.Empty,
        $"public sealed class {typeName}",
        "{",
        $"    public int Value => {value};",
        "}",
        string.Empty);
}

static async Task RunWorkflowSmokeAsync(McpClient client, TestProfile profile)
{
    var changedFiles = CreateWorkflowChangedFiles(profile);
    var buildOutput = CreateWorkflowBuildOutput(profile);

    var investigation = await CallAsync(client, "start_csharp_investigation", profile.WithTarget(new Dictionary<string, object?>
    {
        ["problemText"] = "Workflow E2E should keep build and changed-file evidence ahead of background diagnostics.",
        ["symbolQuery"] = profile.SymbolQuery,
        ["changedFiles"] = changedFiles,
        ["buildOutput"] = buildOutput,
        ["projectName"] = string.IsNullOrWhiteSpace(profile.ProjectName) ? null : profile.ProjectName,
        ["maxSymbols"] = 10,
        ["maxDiagnostics"] = 20,
        ["maxRelatedItems"] = 5,
        ["includeVisualStudioBuildOutput"] = false,
        ["includeGeneratedCode"] = false,
    }));

    using (var payload = ParseToolPayload(investigation))
    {
        var item = RequireSingleWorkflowItem(payload, "investigation");
        var primaryFiles = item.GetProperty("primaryFiles");
        var primaryDiagnostics = item.GetProperty("primaryDiagnostics");
        var nextActions = item.GetProperty("recommendedNextActions");
        RequireArrayCountAtLeast(primaryFiles, 1, "Workflow investigation primaryFiles");
        RequireArrayCountAtLeast(primaryDiagnostics, 1, "Workflow investigation primaryDiagnostics");
        RequireArrayCountAtLeast(nextActions, 1, "Workflow investigation recommendedNextActions");
        RequireArrayCountAtMost(item.GetProperty("references"), 5, "Workflow investigation references");
        RequireArrayCountAtMost(item.GetProperty("callers"), 5, "Workflow investigation callers");
        RequireArrayCountAtMost(item.GetProperty("callees"), 5, "Workflow investigation callees");
        RequireArrayCountAtMost(item.GetProperty("relatedTests"), 5, "Workflow investigation relatedTests");

        Console.WriteLine(
            "WORKFLOW_INVESTIGATION status={0} primaryFiles={1} primaryDiagnostics={2} nextActions={3} partial={4}",
            item.GetProperty("status").GetString(),
            primaryFiles.GetArrayLength(),
            primaryDiagnostics.GetArrayLength(),
            nextActions.GetArrayLength(),
            payload.RootElement.GetProperty("isPartial").GetBoolean());
    }

    var verification = await CallAsync(client, "plan_csharp_verification", profile.WithTarget(new Dictionary<string, object?>
    {
        ["symbolQuery"] = profile.SymbolQuery,
        ["changedFiles"] = changedFiles,
        ["buildOutput"] = buildOutput,
        ["projectName"] = string.IsNullOrWhiteSpace(profile.ProjectName) ? null : profile.ProjectName,
        ["maxDiagnostics"] = 20,
        ["maxRelatedTests"] = 5,
        ["includeVisualStudioBuildOutput"] = false,
        ["includeGeneratedCode"] = false,
    }));

    using (var payload = ParseToolPayload(verification))
    {
        var item = RequireSingleWorkflowItem(payload, "verification");
        var commands = item.GetProperty("recommendedCommands");
        RequireArrayCountAtLeast(commands, 1, "Workflow verification recommendedCommands");
        if (!item.TryGetProperty("recommendedNextActions", out _))
        {
            throw new InvalidOperationException("Workflow verification did not include recommendedNextActions.");
        }

        Console.WriteLine(
            "WORKFLOW_VERIFICATION status={0} commands={1} affectedProjects={2} partial={3}",
            item.GetProperty("status").GetString(),
            commands.GetArrayLength(),
            item.GetProperty("affectedProjects").GetArrayLength(),
            payload.RootElement.GetProperty("isPartial").GetBoolean());
    }

    var review = await CallAsync(client, "review_csharp_change", profile.WithTarget(new Dictionary<string, object?>
    {
        ["problemText"] = "Workflow E2E should produce a compact read-only change review package.",
        ["symbolQuery"] = profile.SymbolQuery,
        ["changedFiles"] = changedFiles,
        ["buildOutput"] = buildOutput,
        ["projectName"] = string.IsNullOrWhiteSpace(profile.ProjectName) ? null : profile.ProjectName,
        ["maxDiagnostics"] = 20,
        ["maxRelatedItems"] = 5,
        ["includeGeneratedCode"] = false,
    }));

    using (var payload = ParseToolPayload(review))
    {
        var item = RequireSingleWorkflowItem(payload, "review");
        RequireArrayCountAtLeast(item.GetProperty("primaryFiles"), 1, "Workflow review primaryFiles");
        RequireArrayCountAtLeast(item.GetProperty("recommendedNextActions"), 1, "Workflow review recommendedNextActions");
        if (!item.TryGetProperty("candidateEditLocations", out _))
        {
            throw new InvalidOperationException("Workflow review did not include candidateEditLocations.");
        }

        Console.WriteLine(
            "WORKFLOW_REVIEW status={0} findings={1} risks={2} actions={3} partial={4}",
            item.GetProperty("status").GetString(),
            item.GetProperty("findings").GetArrayLength(),
            item.GetProperty("publicApiRisks").GetArrayLength(),
            item.GetProperty("recommendedNextActions").GetArrayLength(),
            payload.RootElement.GetProperty("isPartial").GetBoolean());
    }
}

static JsonElement RequireSingleWorkflowItem(JsonDocument payload, string name)
{
    var items = payload.RootElement.GetProperty("items");
    if (items.GetArrayLength() != 1)
    {
        throw new InvalidOperationException($"Workflow {name} should return exactly one item: " + payload.RootElement);
    }

    var item = items[0];
    if (!item.TryGetProperty("taskContext", out _))
    {
        throw new InvalidOperationException($"Workflow {name} did not include taskContext.");
    }

    return item;
}

static void RequireArrayCountAtLeast(JsonElement array, int minimum, string name)
{
    if (array.GetArrayLength() < minimum)
    {
        throw new InvalidOperationException($"{name} expected at least {minimum} item(s).");
    }
}

static void RequireArrayCountAtMost(JsonElement array, int maximum, string name)
{
    if (array.GetArrayLength() > maximum)
    {
        throw new InvalidOperationException($"{name} expected at most {maximum} item(s).");
    }
}

static string[] CreateWorkflowChangedFiles(TestProfile profile)
{
    var configured = Environment.GetEnvironmentVariable("CODE_NAVIGATOR_TEST_CHANGED_FILES");
    if (!string.IsNullOrWhiteSpace(configured))
    {
        return configured
            .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    return string.IsNullOrWhiteSpace(profile.DocumentPath)
        ? Array.Empty<string>()
        : new[] { profile.DocumentPath };
}

static string CreateWorkflowBuildOutput(TestProfile profile)
{
    var configured = Environment.GetEnvironmentVariable("CODE_NAVIGATOR_TEST_BUILD_OUTPUT");
    if (!string.IsNullOrWhiteSpace(configured))
    {
        return configured;
    }

    var path = string.IsNullOrWhiteSpace(profile.DocumentPath)
        ? Path.Combine(profile.SolutionPath, "WorkflowE2e.cs")
        : profile.DocumentPath;
    var project = string.IsNullOrWhiteSpace(profile.ProjectName)
        ? profile.SolutionPath
        : profile.ProjectName;

    return string.Format(
        "{0}(1,1): error CS0103: The name 'workflowE2eMissingValue' does not exist in the current context [{1}]",
        path,
        project);
}

static async Task RunLineEndpointPocAsync(
    McpClient client,
    TestProfile profile,
    string queryText,
    string expectedName,
    string expectedContainingType)
{
    var search = await CallAsync(client, "search_csharp_symbols", profile.WithTarget(new Dictionary<string, object?>
    {
        ["queryText"] = queryText,
        ["maxResults"] = 25,
        ["includeGeneratedCode"] = false,
    }));

    string symbolKey;
    using (var payload = ParseToolPayload(search))
    {
        var item = FindSymbol(payload.RootElement.GetProperty("items"), expectedName, expectedContainingType);
        symbolKey = item.GetProperty("key").GetProperty("value").GetString()
            ?? throw new InvalidOperationException($"No symbol key for {queryText}.");
        Console.WriteLine(
            "LC_LINE_SYMBOL query={0} key={1} file={2} line={3}",
            queryText,
            symbolKey,
            item.GetProperty("span").GetProperty("filePath").GetString(),
            item.GetProperty("span").GetProperty("startLine").GetInt32());
    }

    var references = await CallAsync(client, "find_csharp_references", profile.WithTarget(new Dictionary<string, object?>
    {
        ["symbolKey"] = symbolKey,
        ["includeGeneratedCode"] = false,
    }));

    using (var payload = ParseToolPayload(references))
    {
        var items = payload.RootElement.GetProperty("items");
        var roleCounts = CountRoles(items);
        Console.WriteLine(
            "LC_LINE_REFS query={0} total={1} reads={2} writes={3} invocations={4} unknown={5}",
            queryText,
            items.GetArrayLength(),
            roleCounts.GetValueOrDefault("Read"),
            roleCounts.GetValueOrDefault("Write"),
            roleCounts.GetValueOrDefault("Invocation"),
            roleCounts.GetValueOrDefault("Unknown"));
        PrintFirstRoleSample(queryText, items, "Read");
        PrintFirstRoleSample(queryText, items, "Write");
        PrintFirstRoleSample(queryText, items, "Invocation");
    }
}

static async Task RunImplementationsPocAsync(
    McpClient client,
    TestProfile profile,
    string queryText,
    string expectedName,
    string expectedContainingType)
{
    var symbolKey = await SearchSymbolKeyAsync(client, profile, queryText, expectedName, expectedContainingType);
    var implementations = await CallAsync(client, "find_csharp_implementations", profile.WithTarget(new Dictionary<string, object?>
    {
        ["symbolKey"] = symbolKey,
        ["includeGeneratedCode"] = false,
    }));

    using var payload = ParseToolPayload(implementations);
    var items = payload.RootElement.GetProperty("items");
    Console.WriteLine(
        "IMPLEMENTATIONS query={0} total={1} role={2}",
        queryText,
        items.GetArrayLength(),
        items.GetArrayLength() == 0 ? string.Empty : items[0].GetProperty("role").GetString());
    PrintFirstSymbolReferenceSample("IMPLEMENTATIONS_SAMPLE", queryText, items);
}

static async Task RunOverridesPocAsync(
    McpClient client,
    TestProfile profile,
    string queryText,
    string expectedName,
    string expectedContainingType)
{
    var symbolKey = await SearchSymbolKeyAsync(client, profile, queryText, expectedName, expectedContainingType);
    var overrides = await CallAsync(client, "find_csharp_overrides", profile.WithTarget(new Dictionary<string, object?>
    {
        ["symbolKey"] = symbolKey,
        ["includeGeneratedCode"] = false,
    }));

    using var payload = ParseToolPayload(overrides);
    var items = payload.RootElement.GetProperty("items");
    Console.WriteLine(
        "OVERRIDES query={0} total={1} role={2}",
        queryText,
        items.GetArrayLength(),
        items.GetArrayLength() == 0 ? string.Empty : items[0].GetProperty("role").GetString());
    PrintFirstSymbolReferenceSample("OVERRIDES_SAMPLE", queryText, items);
}

static async Task<string> SearchSymbolKeyAsync(
    McpClient client,
    TestProfile profile,
    string queryText,
    string expectedName,
    string expectedContainingType)
{
    var search = await CallAsync(client, "search_csharp_symbols", profile.WithTarget(new Dictionary<string, object?>
    {
        ["queryText"] = queryText,
        ["maxResults"] = 50,
        ["includeGeneratedCode"] = false,
    }));

    using var payload = ParseToolPayload(search);
    var item = FindSymbol(payload.RootElement.GetProperty("items"), expectedName, expectedContainingType);
    var symbolKey = item.GetProperty("key").GetProperty("value").GetString()
        ?? throw new InvalidOperationException($"No symbol key for {queryText}.");
    Console.WriteLine(
        "SYMBOL query={0} key={1} file={2} line={3}",
        queryText,
        symbolKey,
        item.GetProperty("span").GetProperty("filePath").GetString(),
        item.GetProperty("span").GetProperty("startLine").GetInt32());
    return symbolKey;
}

static JsonElement FindSymbol(JsonElement items, string expectedName, string expectedContainingType)
{
    foreach (var item in items.EnumerateArray())
    {
        if (string.Equals(item.GetProperty("name").GetString(), expectedName, StringComparison.Ordinal)
            && string.Equals(item.GetProperty("containingType").GetString(), expectedContainingType, StringComparison.Ordinal))
        {
            return item.Clone();
        }
    }

    throw new InvalidOperationException($"Could not find symbol {expectedContainingType}.{expectedName}.");
}

static bool ContainsDocumentSymbol(JsonElement items, string symbolName)
{
    foreach (var item in items.EnumerateArray())
    {
        if (string.Equals(
            item.GetProperty("symbol").GetProperty("name").GetString(),
            symbolName,
            StringComparison.Ordinal))
        {
            return true;
        }
    }

    return false;
}

static Dictionary<string, int> CountRoles(JsonElement items)
{
    var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    foreach (var item in items.EnumerateArray())
    {
        var role = item.GetProperty("role").GetString() ?? "Unknown";
        counts[role] = counts.TryGetValue(role, out var count) ? count + 1 : 1;
    }

    return counts;
}

static void PrintFirstRoleSample(string queryText, JsonElement items, string role)
{
    foreach (var item in items.EnumerateArray())
    {
        if (!string.Equals(item.GetProperty("role").GetString(), role, StringComparison.OrdinalIgnoreCase))
        {
            continue;
        }

        var span = item.GetProperty("span");
        Console.WriteLine(
            "LC_LINE_SAMPLE query={0} role={1} file={2} line={3} column={4}",
            queryText,
            role,
            span.GetProperty("filePath").GetString(),
            span.GetProperty("startLine").GetInt32(),
            span.GetProperty("startColumn").GetInt32());
        return;
    }
}

static void PrintFirstSymbolReferenceSample(string prefix, string queryText, JsonElement items)
{
    if (items.GetArrayLength() == 0)
    {
        return;
    }

    var item = items[0];
    var span = item.GetProperty("span");
    var symbol = item.GetProperty("symbol");
    Console.WriteLine(
        "{0} query={1} symbol={2}.{3} file={4} line={5}",
        prefix,
        queryText,
        symbol.GetProperty("containingType").GetString(),
        symbol.GetProperty("name").GetString(),
        span.GetProperty("filePath").GetString(),
        span.GetProperty("startLine").GetInt32());
}

static void PrintFirstDocumentSymbolSample(JsonElement items, string expectedName)
{
    foreach (var item in items.EnumerateArray())
    {
        var symbol = item.GetProperty("symbol");
        if (!string.Equals(symbol.GetProperty("name").GetString(), expectedName, StringComparison.Ordinal))
        {
            continue;
        }

        var span = symbol.GetProperty("span");
        Console.WriteLine(
            "DOC_SYMBOL_SAMPLE name={0} depth={1} file={2} line={3}",
            expectedName,
            item.GetProperty("depth").GetInt32(),
            span.GetProperty("filePath").GetString(),
            span.GetProperty("startLine").GetInt32());
        return;
    }
}

static void PrintFirstCallGraphSample(string prefix, string queryText, JsonElement items)
{
    if (items.GetArrayLength() == 0)
    {
        return;
    }

    var item = items[0];
    var span = item.GetProperty("span");
    var source = item.GetProperty("source");
    var target = item.GetProperty("target");
    Console.WriteLine(
        "{0} query={1} source={2}.{3} target={4}.{5} kind={6} file={7} line={8}",
        prefix,
        queryText,
        source.GetProperty("containingType").GetString(),
        source.GetProperty("name").GetString(),
        target.GetProperty("containingType").GetString(),
        target.GetProperty("name").GetString(),
        item.GetProperty("kind").GetString(),
        span.GetProperty("filePath").GetString(),
        span.GetProperty("startLine").GetInt32());
}

static int MaxCallGraphDepth(JsonElement items)
{
    var maxDepth = 0;
    foreach (var item in items.EnumerateArray())
    {
        maxDepth = Math.Max(maxDepth, item.GetProperty("depth").GetInt32());
    }

    return maxDepth;
}

static void PrintFirstImpactFileSample(string prefix, string queryText, JsonElement items)
{
    if (items.GetArrayLength() == 0)
    {
        return;
    }

    var item = items[0];
    Console.WriteLine(
        "{0} query={1} project={2} file={3} count={4}",
        prefix,
        queryText,
        item.GetProperty("projectName").GetString(),
        item.GetProperty("filePath").GetString(),
        item.GetProperty("count").GetInt32());
}

static void PrintFirstDerivedTypeSample(string prefix, string queryText, JsonElement items)
{
    if (items.GetArrayLength() == 0)
    {
        return;
    }

    var item = items[0];
    var symbol = item.GetProperty("symbol");
    var span = symbol.GetProperty("span");
    Console.WriteLine(
        "{0} query={1} symbol={2}.{3} depth={4} direct={5} file={6} line={7}",
        prefix,
        queryText,
        symbol.GetProperty("containingType").GetString(),
        symbol.GetProperty("name").GetString(),
        item.GetProperty("depth").GetInt32(),
        item.GetProperty("isDirect").GetBoolean(),
        span.GetProperty("filePath").GetString(),
        span.GetProperty("startLine").GetInt32());
}

static void PrintFirstRelatedTestSample(string prefix, string queryText, JsonElement items)
{
    if (items.GetArrayLength() == 0)
    {
        return;
    }

    var item = items[0];
    var span = item.GetProperty("span");
    Console.WriteLine(
        "{0} query={1} project={2} class={3} method={4} reasons={5} file={6} line={7}",
        prefix,
        queryText,
        item.GetProperty("projectName").GetString(),
        item.GetProperty("testClass").GetString(),
        item.GetProperty("testMethod").GetString(),
        string.Join("+", item.GetProperty("matchReasons").EnumerateArray().Select(reason => reason.GetString())),
        span.GetProperty("filePath").GetString(),
        span.GetProperty("startLine").GetInt32());
}

static void PrintDiagnostics(string prefix, JsonElement diagnostics)
{
    foreach (var diagnostic in diagnostics.EnumerateArray())
    {
        Console.WriteLine("{0} {1}", prefix, diagnostic.GetString());
    }
}

static bool DiagnosticsContain(JsonDocument payload, string text)
{
    foreach (var diagnostic in payload.RootElement.GetProperty("diagnostics").EnumerateArray())
    {
        if (diagnostic.GetString()?.Contains(text, StringComparison.Ordinal) == true)
        {
            return true;
        }
    }

    return false;
}

static JsonDocument ParseToolPayload(string resultJson)
{
    using var result = JsonDocument.Parse(resultJson);
    var content = result.RootElement.GetProperty("content")[0].GetProperty("text").GetString()
        ?? throw new InvalidOperationException("Tool result did not contain text content.");

    return JsonDocument.Parse(content);
}

static string Serialize<T>(T value)
{
    return JsonSerializer.Serialize(value, new JsonSerializerOptions
    {
        WriteIndented = false,
    });
}

sealed record TestProfile(
    string Name,
    string SolutionPath,
    string TargetPipeName,
    string TargetInstanceId,
    string SymbolQuery,
    string ExpectedSymbolName,
    string ExpectedContainingType,
    string ProjectName,
    string DocumentPath,
    int ContextLine,
    int ContextColumn)
{
    public bool IsCSharpNavigatorProfile => string.Equals(Name, "realproject", StringComparison.OrdinalIgnoreCase);

    public bool IsDebugControlProfile => string.Equals(Name, "debug-control", StringComparison.OrdinalIgnoreCase);

    public bool IsToolSchemaProfile => string.Equals(Name, "tool-schema", StringComparison.OrdinalIgnoreCase);

    public bool IsAgenticResourcesProfile => string.Equals(Name, "agentic-resources", StringComparison.OrdinalIgnoreCase);

    public bool IsOutputWindowProfile => string.Equals(Name, "output-window", StringComparison.OrdinalIgnoreCase);

    public bool IsCodeFixProfile => string.Equals(Name, "codefix", StringComparison.OrdinalIgnoreCase);

    public bool IsWorkflowProfile => string.Equals(Name, "workflow", StringComparison.OrdinalIgnoreCase);

    public bool EnforcesGenericSampleExpectations =>
        string.Equals(Name, "generic", StringComparison.OrdinalIgnoreCase)
        && string.Equals(ExpectedSymbolName, "Add", StringComparison.Ordinal)
        && string.Equals(ExpectedContainingType, "CodeNavigator.Sample.Calculator", StringComparison.Ordinal)
        && string.Equals(ProjectName, "CodeNavigator.Sample", StringComparison.Ordinal);

    public Dictionary<string, object?> WithTarget(Dictionary<string, object?> arguments)
    {
        if (!string.IsNullOrWhiteSpace(TargetPipeName))
        {
            arguments["targetPipeName"] = TargetPipeName;
        }
        else if (!string.IsNullOrWhiteSpace(TargetInstanceId))
        {
            arguments["targetInstanceId"] = TargetInstanceId;
        }
        else if (!string.IsNullOrWhiteSpace(SolutionPath))
        {
            arguments["targetSolutionPath"] = SolutionPath;
        }

        return arguments;
    }

    public static TestProfile Load(string workspaceRoot)
    {
        var profileName = ReadSetting("CODE_NAVIGATOR_TEST_PROFILE")
            ?? ReadSetting("CODE_NAVIGATOR_BENCHMARK_PROFILE")
            ?? "generic";
        var targetPipeName = ReadSetting("CODE_NAVIGATOR_TEST_TARGET_PIPE_NAME")
            ?? ReadSetting("CODE_NAVIGATOR_BENCHMARK_TARGET_PIPE_NAME")
            ?? string.Empty;
        var targetInstanceId = ReadSetting("CODE_NAVIGATOR_TEST_TARGET_INSTANCE_ID")
            ?? ReadSetting("CODE_NAVIGATOR_BENCHMARK_TARGET_INSTANCE_ID")
            ?? string.Empty;

        if (string.Equals(profileName, "generic", StringComparison.OrdinalIgnoreCase))
        {
            var sampleSolution = Path.Combine(
                workspaceRoot,
                "artifacts",
                "sample-csharp-solution",
                "CodeNavigator.Sample.sln");
            var sampleDocument = Path.Combine(
                workspaceRoot,
                "artifacts",
                "sample-csharp-solution",
                "src",
                "CodeNavigator.Sample",
                "Calculator.cs");

            return new TestProfile(
                "generic",
                ReadSetting("CODE_NAVIGATOR_TEST_SOLUTION") ?? sampleSolution,
                targetPipeName,
                targetInstanceId,
                ReadSetting("CODE_NAVIGATOR_TEST_SYMBOL") ?? "Calculator.Add",
                ReadSetting("CODE_NAVIGATOR_TEST_EXPECTED_NAME") ?? "Add",
                ReadSetting("CODE_NAVIGATOR_TEST_EXPECTED_CONTAINING_TYPE") ?? "CodeNavigator.Sample.Calculator",
                ReadSetting("CODE_NAVIGATOR_TEST_PROJECT") ?? "CodeNavigator.Sample",
                ReadSetting("CODE_NAVIGATOR_TEST_DOCUMENT") ?? sampleDocument,
                ReadIntSetting("CODE_NAVIGATOR_TEST_LINE", 5),
                ReadIntSetting("CODE_NAVIGATOR_TEST_COLUMN", 16));
        }

        if (string.Equals(profileName, "tool-schema", StringComparison.OrdinalIgnoreCase))
        {
            return new TestProfile(
                "tool-schema",
                string.Empty,
                targetPipeName,
                targetInstanceId,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                1,
                1);
        }

        if (string.Equals(profileName, "agentic-resources", StringComparison.OrdinalIgnoreCase))
        {
            return new TestProfile(
                "agentic-resources",
                string.Empty,
                targetPipeName,
                targetInstanceId,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                1,
                1);
        }

        if (string.Equals(profileName, "workflow", StringComparison.OrdinalIgnoreCase))
        {
            var sampleSolution = Path.Combine(
                workspaceRoot,
                "artifacts",
                "sample-csharp-solution",
                "CodeNavigator.Sample.sln");
            var sampleDocument = Path.Combine(
                workspaceRoot,
                "artifacts",
                "sample-csharp-solution",
                "src",
                "CodeNavigator.Sample",
                "Calculator.cs");

            return new TestProfile(
                "workflow",
                ReadSetting("CODE_NAVIGATOR_TEST_SOLUTION") ?? sampleSolution,
                targetPipeName,
                targetInstanceId,
                ReadSetting("CODE_NAVIGATOR_TEST_SYMBOL") ?? "Calculator.Add",
                ReadSetting("CODE_NAVIGATOR_TEST_EXPECTED_NAME") ?? "Add",
                ReadSetting("CODE_NAVIGATOR_TEST_EXPECTED_CONTAINING_TYPE") ?? "CodeNavigator.Sample.Calculator",
                ReadSetting("CODE_NAVIGATOR_TEST_PROJECT") ?? "CodeNavigator.Sample",
                ReadSetting("CODE_NAVIGATOR_TEST_DOCUMENT") ?? sampleDocument,
                ReadIntSetting("CODE_NAVIGATOR_TEST_LINE", 5),
                ReadIntSetting("CODE_NAVIGATOR_TEST_COLUMN", 16));
        }

        if (string.Equals(profileName, "output-window", StringComparison.OrdinalIgnoreCase))
        {
            return new TestProfile(
                "output-window",
                ReadSetting("CODE_NAVIGATOR_TEST_SOLUTION")
                    ?? ReadSetting("VisualStudioBridge__SolutionPath")
                    ?? string.Empty,
                targetPipeName,
                targetInstanceId,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                1,
                1);
        }

        if (string.Equals(profileName, "codefix", StringComparison.OrdinalIgnoreCase))
        {
            var sampleSolution = Path.Combine(
                workspaceRoot,
                "artifacts",
                "sample-csharp-solution",
                "CodeNavigator.Sample.sln");
            var sampleDocument = Path.Combine(
                workspaceRoot,
                "artifacts",
                "sample-csharp-solution",
                "src",
                "CodeNavigator.Sample",
                "CodeFixSingleSample.cs");

            return new TestProfile(
                "codefix",
                ReadSetting("CODE_NAVIGATOR_TEST_SOLUTION") ?? sampleSolution,
                targetPipeName,
                targetInstanceId,
                string.Empty,
                string.Empty,
                string.Empty,
                ReadSetting("CODE_NAVIGATOR_TEST_PROJECT") ?? "CodeNavigator.Sample",
                ReadSetting("CODE_NAVIGATOR_TEST_DOCUMENT") ?? sampleDocument,
                1,
                1);
        }

        if (string.Equals(profileName, "debug-control", StringComparison.OrdinalIgnoreCase))
        {
            var sampleSolution = Path.Combine(
                workspaceRoot,
                "artifacts",
                "debug-control-sample",
                "DebugControl.Sample.sln");
            var sampleDocument = Path.Combine(
                workspaceRoot,
                "artifacts",
                "debug-control-sample",
                "src",
                "DebugControl.Sample.App",
                "Program.cs");

            return new TestProfile(
                "debug-control",
                ReadSetting("CODE_NAVIGATOR_TEST_SOLUTION") ?? sampleSolution,
                targetPipeName,
                targetInstanceId,
                ReadSetting("CODE_NAVIGATOR_TEST_SYMBOL") ?? "SampleCalculator.Add",
                ReadSetting("CODE_NAVIGATOR_TEST_EXPECTED_NAME") ?? "Add",
                ReadSetting("CODE_NAVIGATOR_TEST_EXPECTED_CONTAINING_TYPE") ?? "DebugControl.Sample.App.SampleCalculator",
                ReadSetting("CODE_NAVIGATOR_TEST_PROJECT") ?? "DebugControl.Sample.App",
                ReadSetting("CODE_NAVIGATOR_TEST_DOCUMENT") ?? sampleDocument,
                ReadIntSetting("CODE_NAVIGATOR_TEST_LINE", 15),
                ReadIntSetting("CODE_NAVIGATOR_TEST_COLUMN", 25));
        }

        return new TestProfile(
            "realproject",
            ReadSetting("CODE_NAVIGATOR_TEST_SOLUTION")
                ?? ReadSetting("VisualStudioBridge__SolutionPath")
                ?? @"D:\Samples\SampleWorkspace\SampleWorkspace.sln",
            targetPipeName,
            targetInstanceId,
            ReadSetting("CODE_NAVIGATOR_TEST_SYMBOL") ?? "SetProps",
            ReadSetting("CODE_NAVIGATOR_TEST_EXPECTED_NAME") ?? "SetProps",
            ReadSetting("CODE_NAVIGATOR_TEST_EXPECTED_CONTAINING_TYPE") ?? "SampleWorkspace.Core.LcObject",
            ReadSetting("CODE_NAVIGATOR_TEST_PROJECT") ?? "SampleWorkspace.Core",
            ReadSetting("CODE_NAVIGATOR_TEST_DOCUMENT")
                ?? @"D:\Samples\SampleWorkspace\src\SampleWorkspace.Core\Elements\Basic\LcLine.cs",
            ReadIntSetting("CODE_NAVIGATOR_TEST_LINE", 15),
            ReadIntSetting("CODE_NAVIGATOR_TEST_COLUMN", 20));
    }

    private static string? ReadSetting(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static int ReadIntSetting(string name, int defaultValue)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return int.TryParse(value, out var parsed) ? parsed : defaultValue;
    }
}
