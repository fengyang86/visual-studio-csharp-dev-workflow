using System.IO.Pipes;
using System.Text;
using Microsoft.Extensions.Options;
using VisualStudio.CSharpNavigator.Protocol;
using VisualStudio.CSharpNavigator.Server.Bridge;

namespace VisualStudio.CSharpNavigator.Server.Tests;

public sealed class BridgeOperationCompatibilityTests
{
    [Fact]
    public async Task LegacyMutationResponseIsKnownButRecoverySupportIsUnconfirmed()
    {
        var pipeName = "CSharpWorkflow.Legacy." + Guid.NewGuid().ToString("N");
        var server = Serve(pipeName, """{"protocolVersion":"1","result":{"items":[{}],"isPartial":false,"diagnostics":[]}}""");
        var result = await Create(pipeName).ApplyRenameAsync(new RenameApplyRequest(), default);
        await server;
        Assert.False(result.IsPartial);
        Assert.Single(result.Items);
        Assert.Contains(result.Diagnostics, item => item.Contains("tracking=Unconfirmed"));
        Assert.Contains(result.Diagnostics, item => item.StartsWith("BridgeOperationTrackingUnconfirmed:", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Diagnostics, item => item.StartsWith("OperationOutcomeUnconfirmed:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task OldBridgeWithoutRecoveryMethodDoesNotReturnAnEmptySuccess()
    {
        var pipeName = "CSharpWorkflow.Legacy." + Guid.NewGuid().ToString("N");
        var server = Serve(pipeName, """{"protocolVersion":"1","error":{"code":"UnsupportedMethod","message":"GetOperationStatus"}}""");
        var result = await Create(pipeName).GetOperationStatusAsync(new BridgeOperationStatusRequest { RequestId = "old" }, default);
        await server;
        Assert.True(result.IsPartial);
        Assert.Empty(result.Items);
        Assert.Contains(result.Diagnostics, item => item.Contains("UnsupportedMethod"));
    }

    [Fact]
    public async Task DisconnectedMutationIncludesReceiptAndCannotBeAssumedUnexecuted()
    {
        var pipeName = "CSharpWorkflow.Disconnected." + Guid.NewGuid().ToString("N");
        var server = Serve(pipeName, null);
        var result = await Create(pipeName).ApplyRenameAsync(new RenameApplyRequest(), default);
        await server;
        Assert.True(result.IsPartial);
        Assert.Contains(result.Diagnostics, item => item.Contains("BridgeOperationReceipt: requestId=") && item.Contains(pipeName));
        Assert.Contains(result.Diagnostics, item => item.StartsWith("OperationOutcomeUnconfirmed:", StringComparison.Ordinal));
    }

    private static NamedPipeVisualStudioWorkspaceBridge Create(string pipeName) => new(Options.Create(new NamedPipeBridgeOptions
    {
        PipeName = pipeName,
        ConnectTimeoutMilliseconds = 5000,
    }));

    private static async Task Serve(string pipeName, string? response)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        await pipe.WaitForConnectionAsync(timeout.Token);
        using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
        Assert.NotNull(await reader.ReadLineAsync(timeout.Token));
        if (response is not null)
        {
            await using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
            await writer.WriteLineAsync(response.AsMemory(), timeout.Token);
        }
    }
}
