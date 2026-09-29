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
    private int _writingRecord;
    private string? _extensionAssemblyVersion;
    private string? _extensionFileVersion;
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
        // The discovery record only needs the solution identity. The full status
        // (DTE reads, per-project XML parsing, FileVersionInfo) is pure overhead
        // on a 10-second heartbeat.
        var statusResult = await _queryService.GetWorkspaceStatusResultAsync(
            InstanceId,
            ProcessId,
            cancellationToken,
            snapshotOnly: true).ConfigureAwait(false);
        var status = statusResult.Items.Count > 0 ? statusResult.Items[0] : new WorkspaceStatus();
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

        // Skip a beat when the previous write is still in flight instead of
        // racing two writers on the same temp file.
        if (Interlocked.CompareExchange(ref _writingRecord, 1, 0) != 0)
        {
            BridgeLog.Warning("Skipped a bridge heartbeat because the previous record write is still running.");
            return;
        }

        try
        {
            var json = JsonSerializer.Serialize(instance, JsonOptions);
            var tempPath = _recordPath + ".tmp." + Guid.NewGuid().ToString("N");
            try
            {
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
            finally
            {
                if (File.Exists(tempPath))
                {
                    try
                    {
                        File.Delete(tempPath);
                    }
                    catch (IOException)
                    {
                    }
                }
            }
        }
        finally
        {
            Volatile.Write(ref _writingRecord, 0);
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

    // Both values are immutable for the lifetime of the VS process; cache them
    // instead of calling FileVersionInfo on every heartbeat.
    private string GetExtensionAssemblyVersion()
    {
        return _extensionAssemblyVersion ??= typeof(BridgeInstanceRegistry).Assembly.GetName().Version?.ToString() ?? string.Empty;
    }

    private string GetExtensionFileVersion()
    {
        if (_extensionFileVersion is not null)
        {
            return _extensionFileVersion;
        }

        var assemblyPath = typeof(BridgeInstanceRegistry).Assembly.Location;
        return _extensionFileVersion = string.IsNullOrWhiteSpace(assemblyPath)
            ? string.Empty
            : FileVersionInfo.GetVersionInfo(assemblyPath).FileVersion ?? string.Empty;
    }
}
