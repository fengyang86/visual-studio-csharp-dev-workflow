using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using VisualStudio.CSharpNavigator.Protocol;
using VisualStudio.CSharpNavigator.Server.Bridge;
using VisualStudio.CSharpNavigator.Server.Tools;

namespace VisualStudio.CSharpNavigator.Server.Tests;

public sealed class BridgeOperationTransportTests
{
    [Fact]
    public async Task ClientCancellationReachesAnotherProcessAndCanBeQueriedAfterReconnect()
    {
        await using var host = await TestHost.Start();
        var bridge = host.CreateBridge();
        using var cancel = new CancellationTokenSource();
        var apply = bridge.ApplyRenameAsync(new RenameApplyRequest { NewName = "wait-cancel" }, cancel.Token);
        var running = await host.WaitForOperation("Running");
        cancel.Cancel();
        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => apply);
        Assert.Contains(running.RequestId, exception.Message);
        var final = await host.WaitForOperation("OutcomeUnknown");
        Assert.Equal(running.RequestId, final.RequestId);
        Assert.True(final.CancellationRequested);
        Assert.True(final.IsTerminal);

        var tools = new BridgeOperationTools(host.CreateBridge());
        var recovered = await tools.GetCSharpOperationStatus(final.RequestId, includeResponse: true, targetPipeName: host.PipeName);
        Assert.Contains("OperationOutcomeUnknown", Assert.Single(recovered.Items).ResponseJson);
        Assert.True(recovered.IsPartial);
    }

    [Fact]
    public async Task DisconnectedCallerCanRecoverCompletedResultWithoutReexecution()
    {
        await using var host = await TestHost.Start();
        var request = PipeBridgeRequestDispatcherTests.Request("lost-response", "ApplyRename", new { newName = "wait-release" });
        await using (var pipe = await host.Connect())
        {
            await using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
            await writer.WriteLineAsync(request);
            Assert.Equal("lost-response", (await host.WaitForOperation("Running")).RequestId);
        }

        await host.Send(PipeBridgeRequestDispatcherTests.Request("", "TestRelease"));
        var terminal = await host.WaitForOperation("Completed");
        Assert.Equal("lost-response", terminal.RequestId);
        var result = await host.CreateBridge().GetOperationStatusAsync(new BridgeOperationStatusRequest
        {
            RequestId = terminal.RequestId,
            IncludeResponse = true,
        }, default);
        Assert.False(result.IsPartial);
        Assert.Contains("\"executionCount\":1", Assert.Single(result.Items).ResponseJson);
        Assert.Contains("\"executionCount\":1", await host.Send(request));
        Assert.Contains("BridgeRequestIdConflict",
            await host.Send(PipeBridgeRequestDispatcherTests.Request("lost-response", "StepOver")));
    }

    [Fact]
    public async Task EarlyCancelAcrossProcessesPreventsBusinessDispatch()
    {
        await using var host = await TestHost.Start();
        await host.Send(PipeBridgeRequestDispatcherTests.Request("", "CancelRequest", new { requestId = "early" }));
        Assert.Contains("RequestCanceledBeforeStart",
            await host.Send(PipeBridgeRequestDispatcherTests.Request("early", "StepOver")));
        var status = await host.WaitForOperation("CanceledBeforeStart");
        Assert.Equal("early", status.RequestId);
        Assert.Contains("\"executionCount\":0", await host.Send(PipeBridgeRequestDispatcherTests.Request("", "GetWorkspaceStatus")));
    }

    [Fact]
    public async Task SuccessfulClientResponseIncludesReceiptAndQueryRequiresExplicitTarget()
    {
        await using var host = await TestHost.Start();
        var bridge = host.CreateBridge();
        var result = await bridge.ApplyRenameAsync(new RenameApplyRequest { NewName = "immediate" }, default);
        Assert.False(result.IsPartial);
        Assert.Contains(result.Diagnostics, value => value.Contains("BridgeOperationReceipt:") && value.Contains("tracking=Confirmed"));
        var tools = new BridgeOperationTools(bridge);
        Assert.Contains("ExplicitTargetRequired", Assert.Single((await tools.GetCSharpOperationStatus()).Diagnostics));
        Assert.Contains("InvalidOperationStatusRequest",
            Assert.Single((await tools.GetCSharpOperationStatus(includeResponse: true, targetPipeName: host.PipeName)).Diagnostics));
        var query = await tools.GetCSharpOperationStatus(targetPipeName: host.PipeName);
        Assert.Equal("Completed", Assert.Single(query.Items).State);
        Assert.Null(Assert.Single(query.Items).ResponseJson);
    }

    private sealed class TestHost : IAsyncDisposable
    {
        private readonly Process _process;
        private readonly Task<string> _stderr;
        public string PipeName { get; }

        private TestHost(Process process, string pipeName)
        {
            _process = process;
            _stderr = process.StandardError.ReadToEndAsync();
            PipeName = pipeName;
        }

        public static async Task<TestHost> Start()
        {
            var pipeName = "CSharpWorkflow.Test." + Guid.NewGuid().ToString("N");
            var start = new ProcessStartInfo("dotnet")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "bridge-test-host", "VisualStudio.CSharpNavigator.Bridge.TestHost.dll"));
            start.ArgumentList.Add(pipeName);
            var process = Process.Start(start)!;
            var host = new TestHost(process, pipeName);
            try
            {
                var ready = await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10));
                Assert.Equal("READY " + process.Id, ready);
                Assert.NotEqual(Environment.ProcessId, process.Id);
                return host;
            }
            catch
            {
                await host.DisposeAsync();
                throw;
            }
        }

        public NamedPipeVisualStudioWorkspaceBridge CreateBridge() => new(Options.Create(new NamedPipeBridgeOptions
        {
            PipeName = PipeName,
            ConnectTimeoutMilliseconds = 5000,
        }));

        public async Task<NamedPipeClientStream> Connect()
        {
            var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            try
            {
                await pipe.ConnectAsync(5000);
                return pipe;
            }
            catch
            {
                pipe.Dispose();
                throw;
            }
        }

        public async Task<string> Send(string request)
        {
            await using var pipe = await Connect();
            using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
            await using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
            await writer.WriteLineAsync(request);
            return (await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5)))!;
        }

        public async Task<BridgeOperationStatus> WaitForOperation(string state)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (true)
            {
                var result = await CreateBridge().GetOperationStatusAsync(new BridgeOperationStatusRequest(), timeout.Token);
                var match = result.Items.FirstOrDefault(item => item.State == state);
                if (match is not null)
                {
                    return match;
                }

                await Task.Delay(10, timeout.Token);
            }
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (!_process.HasExited)
                {
                    await Send(PipeBridgeRequestDispatcherTests.Request("", "TestShutdown"));
                    await _process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
                }
            }
            finally
            {
                if (!_process.HasExited)
                {
                    _process.Kill(entireProcessTree: true);
                    await _process.WaitForExitAsync();
                }

                var exitCode = _process.ExitCode;
                var stderr = await _stderr;
                _process.Dispose();
                Assert.True(exitCode == 0, $"测试桥接进程退出码 {exitCode}: {stderr}");
            }
        }
    }
}
