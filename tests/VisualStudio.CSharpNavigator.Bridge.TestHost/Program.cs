using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using VisualStudio.CSharpNavigator.Protocol;
using VisualStudio.CSharpNavigator.Vsix.Bridge;

// 仅隔离测试使用：真实管道与生产请求调度器，不加载 VS，不执行源码或调试修改。
var pipeName = args.Single();
using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(40));
var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
var executions = 0;
var clients = new ConcurrentBag<Task>();
var dispatcher = new PipeBridgeRequestDispatcher(pipeName, async (request, token) =>
{
    if (request.Method == "TestRelease")
    {
        release.TrySetResult();
    }

    var count = BridgeOperationPolicy.RequiresTracking(request.Method) ? Interlocked.Increment(ref executions) : executions;
    if (request.Payload.ValueKind == JsonValueKind.Object && request.Payload.TryGetProperty("newName", out var behavior))
    {
        if (behavior.GetString() == "wait-cancel")
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
        }
        else if (behavior.GetString() == "wait-release")
        {
            await release.Task.WaitAsync(token);
        }
    }

    return JsonSerializer.Serialize(new
    {
        protocolVersion = "1",
        result = new { items = new[] { new { executionCount = count } }, isPartial = false, diagnostics = Array.Empty<string>() },
    });
}, (request, _) =>
{
    if (request.Payload.ValueKind == JsonValueKind.Object && request.Payload.TryGetProperty("target", out var target))
    {
        var expected = target.Deserialize<VisualStudioBridgeTarget>(PipeBridgeProtocol.JsonOptions) ?? new VisualStudioBridgeTarget();
        if (!WorkspaceTargetIdentity.Matches(expected, new VisualStudioBridgeTarget { PipeName = pipeName, InstanceId = "test-host" }))
        {
            return Task.FromResult<string?>("""{"protocolVersion":"1","error":{"code":"WorkspaceTargetMismatch","message":"测试目标不匹配。"}}""");
        }
    }

    return Task.FromResult<string?>(null);
});

Console.WriteLine("READY " + Environment.ProcessId);
while (!lifetime.IsCancellationRequested)
{
    var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances,
        PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
    try
    {
        await pipe.WaitForConnectionAsync(lifetime.Token);
        clients.Add(HandleAsync(pipe));
    }
    catch (OperationCanceledException)
    {
        pipe.Dispose();
        break;
    }
}

await Task.WhenAll(clients);

async Task HandleAsync(NamedPipeServerStream pipe)
{
    await using (pipe)
    {
        try
        {
            using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
            await using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
            var request = await reader.ReadLineAsync(lifetime.Token);
            if (request is null)
            {
                return;
            }

            using var document = JsonDocument.Parse(request);
            if (document.RootElement.GetProperty("method").GetString() == "TestShutdown")
            {
                await writer.WriteLineAsync("{}");
                lifetime.Cancel();
                return;
            }

            var response = await dispatcher.ExecuteAsync(request, lifetime.Token);
            await writer.WriteLineAsync(response);
        }
        catch (IOException)
        {
            // 测试会故意断开客户端；操作记录保留在调度器中供下一连接查询。
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
        }
    }
}
