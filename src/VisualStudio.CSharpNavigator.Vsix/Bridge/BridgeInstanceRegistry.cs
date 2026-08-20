using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using VisualStudio.CSharpNavigator.Protocol;
using VisualStudio.CSharpNavigator.Vsix.Workspace;

namespace VisualStudio.CSharpNavigator.Vsix.Bridge;

internal sealed class BridgeInstanceRegistry : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly VisualStudioWorkspaceQueryService _queryService;
    private readonly Timer _heartbeatTimer;
    private readonly string _recordPath;
    private bool _disposed;

    public BridgeInstanceRegistry(VisualStudioWorkspaceQueryService queryService)
    {
        _queryService = queryService;
        InstanceId = Guid.NewGuid().ToString("N");
        ProcessId = Process.GetCurrentProcess().Id;
        PipeName = $"VisualStudio.CSharpNavigator.VisualStudioBridge.{ProcessId}.{InstanceId}";
        DiscoveryDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "VisualStudio.CSharpNavigatorMcp",
            "instances");
        _recordPath = Path.Combine(DiscoveryDirectory, InstanceId + ".json");
        _heartbeatTimer = new Timer(_ => _ = WriteRecordSafelyAsync(CancellationToken.None));
    }

    public string InstanceId { get; }

    public int ProcessId { get; }

    public string PipeName { get; }

    public string DiscoveryDirectory { get; }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        try
        {
            Directory.CreateDirectory(DiscoveryDirectory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            BridgeLog.Error($"Failed to create Visual Studio bridge discovery directory '{DiscoveryDirectory}'.", ex);
        }

        await WriteRecordSafelyAsync(CancellationToken.None).ConfigureAwait(false);
        _heartbeatTimer.Change(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));
    }

    public async Task<WorkspaceStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        var status = await _queryService.GetWorkspaceStatusAsync(
            InstanceId,
            ProcessId,
            cancellationToken).ConfigureAwait(false);
        return status;
    }

    public async Task WriteRecordAsync(CancellationToken cancellationToken)
    {
        var status = await GetStatusAsync(cancellationToken).ConfigureAwait(false);
        var instance = new VisualStudioBridgeInstance
        {
            InstanceId = InstanceId,
            ProcessId = ProcessId,
            PipeName = PipeName,
            BridgeProtocolVersion = VisualStudioBridgeInstanceDescriptor.ExpectedBridgeProtocolVersion,
            ExtensionAssemblyVersion = GetExtensionAssemblyVersion(),
            ExtensionFileVersion = GetExtensionFileVersion(),
            SolutionPath = status.SolutionPath,
            SolutionName = status.SolutionName,
            LastSeenUtc = DateTimeOffset.UtcNow,
        };

        var json = JsonSerializer.Serialize(instance, JsonOptions);
        var tempPath = _recordPath + ".tmp";
        File.WriteAllText(tempPath, json);
        if (File.Exists(_recordPath))
        {
            File.Replace(tempPath, _recordPath, null);
        }
        else
        {
            File.Move(tempPath, _recordPath);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _heartbeatTimer.Dispose();
        try
        {
            if (File.Exists(_recordPath))
            {
                File.Delete(_recordPath);
            }
        }
        catch (IOException)
        {
            BridgeLog.Warning($"Failed to delete Visual Studio bridge discovery record '{_recordPath}' because of an IO error.");
        }
        catch (UnauthorizedAccessException)
        {
            BridgeLog.Warning($"Failed to delete Visual Studio bridge discovery record '{_recordPath}' because access was denied.");
        }
    }

    private async Task WriteRecordSafelyAsync(CancellationToken cancellationToken)
    {
        try
        {
            await WriteRecordAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            BridgeLog.Error($"Failed to update Visual Studio bridge discovery record '{_recordPath}'.", ex);
        }
    }

    private static string GetExtensionAssemblyVersion()
    {
        return typeof(BridgeInstanceRegistry).Assembly.GetName().Version?.ToString() ?? string.Empty;
    }

    private static string GetExtensionFileVersion()
    {
        var assemblyPath = typeof(BridgeInstanceRegistry).Assembly.Location;
        return string.IsNullOrWhiteSpace(assemblyPath)
            ? string.Empty
            : FileVersionInfo.GetVersionInfo(assemblyPath).FileVersion ?? string.Empty;
    }
}
