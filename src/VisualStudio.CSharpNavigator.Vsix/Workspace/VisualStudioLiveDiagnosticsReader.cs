using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.LanguageServices;

namespace VisualStudio.CSharpNavigator.Vsix.Workspace;

// Reads the diagnostics Visual Studio has already computed through its live
// background analysis (IDiagnosticAnalyzerService). The service lives in
// Microsoft.CodeAnalysis.Features, which is not shipped in the Roslyn NuGet
// packages - it is only present inside the Visual Studio installation. To keep
// the extension buildable anywhere (CI included) we bridge to it with cached
// reflection instead of a compile-time reference; Visual Studio always loads
// that assembly in-process, and every failure degrades to a clear diagnostic.
//
// Real interface surface (probed on Roslyn 4.14 / VS 18): the classic
// GetDiagnosticsAsync overloads are gone; the service exposes
// GetDiagnosticsForIdsAsync / GetProjectDiagnosticsForIdsAsync returning
// DiagnosticData items. This reader targets any "*ForIdsAsync" overload whose
// first parameter is Project/Solution and whose remaining parameters can be
// defaulted (empty ImmutableArray diagnostic ids = all diagnostics observed so
// far, includeNonSuppressed flags default, CancellationToken passthrough).
internal static class VisualStudioLiveDiagnosticsReader
{
    private const string ServiceAssemblyName = "Microsoft.CodeAnalysis.Features";
    private const string ServiceTypeName = "Microsoft.CodeAnalysis.Diagnostics.IDiagnosticAnalyzerService";

    internal sealed class LiveDiagnosticItem
    {
        public string Id = string.Empty;
        public string Message = string.Empty;
        public string Category = string.Empty;
        public DiagnosticSeverity Severity = DiagnosticSeverity.Hidden;
        public bool IsSuppressed;
        public string? FilePath;
        public int StartLine;
        public int StartColumn;
        public int EndLine;
        public int EndColumn;
    }

    private static MethodInfo? _getAllIdsMethod;
    private static MethodInfo? _getDiagnosticsMethod;
    private static object? _serviceInstance;
    private static string? _resolutionFailure;

    public static async Task<( IReadOnlyList<LiveDiagnosticItem> Items, string? Failure, int DiagnosticIdCount)> GetProjectDiagnosticsAsync(
        VisualStudioWorkspace? workspace,
        IComponentModel componentModel,
        Project project,
        CancellationToken cancellationToken)
    {
        var failure = EnsureResolved(workspace, componentModel);
        if (failure is not null)
        {
            return (Array.Empty<LiveDiagnosticItem>(), failure, 0);
        }

        try
        {
            // Two-step live contract: fetch every diagnostic id the live
            // analyzer knows about, then query diagnostics for those ids (an
            // empty id array means "no diagnostics", not "all").
            string[] diagnosticIds = Array.Empty<string>();
            if (_getAllIdsMethod is not null)
            {
                var idsTask = (Task?)_getAllIdsMethod.Invoke(
                    _serviceInstance,
                    BuildArguments(_getAllIdsMethod, project, cancellationToken));
                if (idsTask is not null)
                {
                    await idsTask.ConfigureAwait(false);
                    var idsResult = idsTask.GetType().GetProperty("Result", BindingFlags.Public | BindingFlags.Instance)?.GetValue(idsTask);
                    if (idsResult is IEnumerable idValues)
                    {
                        diagnosticIds = idValues.Cast<string>().Where(id => id is not null).ToArray();
                    }
                }
            }

            var diagnosticsArguments = BuildArguments(_getDiagnosticsMethod!, project, cancellationToken);
            var parameters = _getDiagnosticsMethod!.GetParameters();
            var immutableStringArray = CreateImmutableStringArray(diagnosticIds);
            var immutableDocumentIdArray = CreateImmutableDocumentIdArray(project);
            for (var index = 0; index < parameters.Length; index++)
            {
                var parameterType = parameters[index].ParameterType;
                if (!parameterType.IsGenericType
                    || parameterType.GetGenericTypeDefinition() != typeof(ImmutableArray<>))
                {
                    continue;
                }

                if (parameterType.GetGenericArguments()[0] == typeof(string))
                {
                    diagnosticsArguments[index] = immutableStringArray;
                }
                else if (parameterType.GetGenericArguments()[0] == typeof(DocumentId))
                {
                    diagnosticsArguments[index] = immutableDocumentIdArray;
                }
            }

            var task = (Task?)_getDiagnosticsMethod!.Invoke(_serviceInstance, diagnosticsArguments);
            if (task is null)
            {
                return (Array.Empty<LiveDiagnosticItem>(), "LiveDiagnosticsUnavailable: the live analyzer service returned no task.", 0);
            }

            await task.ConfigureAwait(false);
            var resultProperty = task.GetType().GetProperty("Result", BindingFlags.Public | BindingFlags.Instance);
            var result = resultProperty?.GetValue(task);
            if (result is not IEnumerable items)
            {
                return (Array.Empty<LiveDiagnosticItem>(), "LiveDiagnosticsUnavailable: the live analyzer service returned an unexpected result shape.", 0);
            }

            var mapped = new List<LiveDiagnosticItem>();
            foreach (var item in items)
            {
                if (item is null)
                {
                    continue;
                }

                var mappedItem = MapDiagnosticData(item);
                if (mappedItem is not null)
                {
                    mapped.Add(mappedItem);
                }
            }

            return (mapped, null, diagnosticIds.Length);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            return (Array.Empty<LiveDiagnosticItem>(), "LiveDiagnosticsFailed: " + ex.InnerException.Message, 0);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return (Array.Empty<LiveDiagnosticItem>(), "LiveDiagnosticsFailed: " + ex.Message, 0);
        }
    }

    private static string? EnsureResolved(VisualStudioWorkspace? workspace, IComponentModel componentModel)
    {
        if (_resolutionFailure is not null)
        {
            return _resolutionFailure;
        }

        if (_serviceInstance is not null && _getDiagnosticsMethod is not null)
        {
            return null;
        }

        try
        {
            var featuresAssembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(assembly => string.Equals(
                    assembly.GetName().Name,
                    ServiceAssemblyName,
                    StringComparison.Ordinal));
            if (featuresAssembly is null)
            {
                return _resolutionFailure = $"LiveDiagnosticsUnavailable: assembly '{ServiceAssemblyName}' is not loaded in this Visual Studio process.";
            }

            var serviceType = featuresAssembly.GetType(ServiceTypeName, throwOnError: false);
            if (serviceType is null)
            {
                return _resolutionFailure = $"LiveDiagnosticsUnavailable: type '{ServiceTypeName}' was not found in '{ServiceAssemblyName}'.";
            }

            // Primary acquisition route: the analyzer service registers as a
            // Roslyn workspace service; the host workspace's service provider
            // is the canonical way to reach it.
            object? serviceInstance = null;
            if (workspace is not null)
            {
                var services = workspace.Services;
                var getServiceGeneric = services.GetType()
                    .GetMethods()
                    .FirstOrDefault(method => method.Name == "GetService"
                        && method.IsGenericMethodDefinition
                        && method.GetParameters().Length == 0);
                if (getServiceGeneric is not null)
                {
                    serviceInstance = getServiceGeneric.MakeGenericMethod(serviceType).Invoke(services, null);
                }
            }

            if (serviceInstance is null)
            {
                var contract = System.ComponentModel.Composition.AttributedModelServices.GetContractName(serviceType);
                serviceInstance = componentModel.DefaultExportProvider?
                    .GetExportedValues<object>(contract)
                    .FirstOrDefault();
            }

            if (serviceInstance is null)
            {
                return _resolutionFailure = "LiveDiagnosticsUnavailable: the live analyzer service is not reachable through workspace services or the global composition.";
            }

            // Accept any "*ForIdsAsync" diagnostics overload whose first
            // parameter is Project/Solution and whose other parameters can be
            // defaulted (empty id arrays, flag defaults, token passthrough);
            // also resolve the matching GetAllDiagnosticIdsAsync for step one.
            MethodInfo? method = null;
            MethodInfo? idsMethod = null;
            foreach (var candidate in serviceType.GetMethods())
            {
                var parameters = candidate.GetParameters();
                if (candidate.Name.StartsWith("Get", StringComparison.Ordinal)
                    && candidate.Name.Contains("ForIdsAsync", StringComparison.Ordinal)
                    && method is null)
                {
                    if (parameters.Length == 0
                        || (parameters[0].ParameterType != typeof(Project)
                            && parameters[0].ParameterType != typeof(Solution)))
                    {
                        continue;
                    }

                    // Every remaining parameter is constructable: value types
                    // (incl. ImmutableArray<string>) via default construction or
                    // declared defaults, reference types (filters) via null.
                    method = candidate;
                    continue;
                }

                if (candidate.Name.Contains("GetAllDiagnosticIds", StringComparison.Ordinal)
                    && idsMethod is null
                    && parameters.Length > 0
                    && (parameters[0].ParameterType == typeof(Project)
                        || parameters[0].ParameterType == typeof(Solution)))
                {
                    idsMethod = candidate;
                }
            }

            if (method is null)
            {
                var allMethodNames = string.Join(", ", serviceType.GetMethods()
                    .Select(candidate => candidate.Name)
                    .Distinct()
                    .OrderBy(name => name, StringComparer.Ordinal)
                    .Take(30));
                return _resolutionFailure = $"LiveDiagnosticsUnavailable: no constructable diagnostics overload. Interface methods: {allMethodNames}";
            }

            _serviceInstance = serviceInstance;
            _getDiagnosticsMethod = method;
            _getAllIdsMethod = idsMethod;
            return null;
        }
        catch (Exception ex)
        {
            return _resolutionFailure = "LiveDiagnosticsUnavailable: " + ex.Message;
        }
    }

    private static object CreateImmutableStringArray(string[] values)
    {
        if (_createImmutableArrayMethod is null)
        {
            return values;
        }

        return _createImmutableArrayMethod.Invoke(null, new object[] { values });
    }

    private static readonly MethodInfo? _createImmutableArrayMethod = typeof(ImmutableArray)
        .GetMethods()
        .FirstOrDefault(method => method.Name == "Create"
            && method.IsGenericMethodDefinition
            && method.GetParameters().Length == 1
            && method.GetParameters()[0].ParameterType.IsArray)
        ?.MakeGenericMethod(typeof(string));

    private static readonly MethodInfo? _createImmutableDocumentIdArrayMethod = typeof(ImmutableArray)
        .GetMethods()
        .FirstOrDefault(method => method.Name == "Create"
            && method.IsGenericMethodDefinition
            && method.GetParameters().Length == 1
            && method.GetParameters()[0].ParameterType.IsArray)
        ?.MakeGenericMethod(typeof(DocumentId));

    private static object CreateImmutableDocumentIdArray(Project project)
    {
        if (_createImmutableDocumentIdArrayMethod is null)
        {
            return Array.Empty<DocumentId>();
        }

        return _createImmutableDocumentIdArrayMethod.Invoke(
            null,
            new object[] { project.DocumentIds.ToArray() });
    }

    private static object?[] BuildArguments(MethodInfo method, Project project, CancellationToken cancellationToken)
    {
        var parameters = method.GetParameters();
        var arguments = new object?[parameters.Length];
        for (var index = 0; index < parameters.Length; index++)
        {
            var parameter = parameters[index];
            if (parameter.ParameterType == typeof(Project))
            {
                arguments[index] = project;
            }
            else if (parameter.ParameterType == typeof(Solution))
            {
                arguments[index] = project.Solution;
            }
            else if (parameter.ParameterType == typeof(CancellationToken))
            {
                arguments[index] = cancellationToken;
            }
            else if (parameter.HasDefaultValue)
            {
                arguments[index] = parameter.DefaultValue;
            }
            else if (parameter.ParameterType.IsValueType)
            {
                arguments[index] = Activator.CreateInstance(parameter.ParameterType);
            }
            else
            {
                arguments[index] = null;
            }
        }

        return arguments;
    }

    private static LiveDiagnosticItem? MapDiagnosticData(object data)
    {
        var type = data.GetType();
        string? ReadString(string name) => type.GetProperty(name)?.GetValue(data) as string;
        var severityRaw = type.GetProperty("Severity")?.GetValue(data);
        if (severityRaw is null && type.GetProperty("DefaultSeverity")?.GetValue(data) is { } fallbackSeverity)
        {
            severityRaw = fallbackSeverity;
        }

        var severity = DiagnosticSeverity.Hidden;
        if (severityRaw is DiagnosticSeverity parsed)
        {
            severity = parsed;
        }
        else if (severityRaw is not null)
        {
            var underlying = Nullable.GetUnderlyingType(severityRaw.GetType());
            if (underlying == typeof(DiagnosticSeverity))
            {
                severity = (DiagnosticSeverity)severityRaw!;
            }
        }

        var item = new LiveDiagnosticItem
        {
            Id = ReadString("Id") ?? string.Empty,
            Message = ReadString("Message") ?? string.Empty,
            Category = ReadString("Category") ?? string.Empty,
            Severity = severity,
            IsSuppressed = type.GetProperty("IsSuppressed")?.GetValue(data) is true,
        };

        // DiagnosticData.DataLocation (DiagnosticDataLocation) carries the
        // mapped file path and source line span.
        if (type.GetProperty("DataLocation")?.GetValue(data) is { } location)
        {
            var locationType = location.GetType();
            item.FilePath = locationType.GetProperty("FilePath")?.GetValue(location) as string;
            if (locationType.GetProperty("SourceSpan")?.GetValue(location) is FileLinePositionSpan span)
            {
                item.StartLine = span.StartLinePosition.Line + 1;
                item.StartColumn = span.StartLinePosition.Character + 1;
                item.EndLine = span.EndLinePosition.Line + 1;
                item.EndColumn = span.EndLinePosition.Character + 1;
            }
            else if (locationType.GetProperty("MappedLineSpan")?.GetValue(location) is FileLinePositionSpan mappedSpan)
            {
                item.StartLine = mappedSpan.StartLinePosition.Line + 1;
                item.StartColumn = mappedSpan.StartLinePosition.Character + 1;
                item.EndLine = mappedSpan.EndLinePosition.Line + 1;
                item.EndColumn = mappedSpan.EndLinePosition.Character + 1;
            }
        }

        if (string.IsNullOrWhiteSpace(item.Id))
        {
            return null;
        }

        return item;
    }
}
