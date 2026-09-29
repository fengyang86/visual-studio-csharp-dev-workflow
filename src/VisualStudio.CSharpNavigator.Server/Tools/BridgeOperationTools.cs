using System.ComponentModel;
using ModelContextProtocol.Server;
using VisualStudio.CSharpNavigator.Abstractions;
using VisualStudio.CSharpNavigator.Protocol;

namespace VisualStudio.CSharpNavigator.Server.Tools;

[McpServerToolType]
public sealed class BridgeOperationTools
{
    private readonly IVisualStudioWorkspaceBridge _bridge;

    public BridgeOperationTools(IVisualStudioWorkspaceBridge bridge)
    {
        _bridge = bridge;
    }

    [McpServerTool(Name = "get_csharp_operation_status", ReadOnly = true, Idempotent = true)]
    [Description("查询指定 VS 实例内最近修改、导航或调试操作的状态及保留响应。用于取消、超时、断线后的结果确认，不重新执行操作；未找到不代表未执行。")]
    public Task<WorkspaceQueryResult<BridgeOperationStatus>> GetCSharpOperationStatus(
        [Description("操作回执中的请求标识。调用被宿主取消、未收到标识时可省略，列出最近记录后人工核对。")]
        string? requestId = null,
        [Description("可选桥接方法过滤，例如 ApplyCodeFix 或 StartDebugging。")]
        string? method = null,
        int maxResults = 10,
        [Description("返回原始响应，只允许在指定 requestId 时启用。Completed 仅表示已返回响应，应检查其中的实际执行结果。")]
        bool includeResponse = false,
        string? targetPipeName = null,
        string? targetInstanceId = null,
        string? targetSolutionPath = null,
        CancellationToken cancellationToken = default)
    {
        var target = new VisualStudioBridgeTarget
        {
            PipeName = targetPipeName?.Trim() ?? string.Empty,
            InstanceId = targetInstanceId?.Trim() ?? string.Empty,
            SolutionPath = targetSolutionPath?.Trim() ?? string.Empty,
        };
        if (target.IsEmpty)
        {
            return Failure("ExplicitTargetRequired: 操作查询必须指定目标 VS 实例、管道或解决方案。");
        }

        if (maxResults < 1 || maxResults > 20 || requestId?.Length > 128
            || (includeResponse && string.IsNullOrWhiteSpace(requestId)))
        {
            return Failure("InvalidOperationStatusRequest: 数量须为 1-20，标识最长 128 字符；原始响应只允许按请求标识读取。");
        }

        if (_bridge is not IVisualStudioOperationBridge operations)
        {
            return Failure("OperationStatusUnsupported: 当前桥接不支持操作查询，不能据此推断操作未执行。");
        }

        return operations.GetOperationStatusAsync(new BridgeOperationStatusRequest
        {
            Target = target,
            RequestId = requestId?.Trim() ?? string.Empty,
            Method = method?.Trim() ?? string.Empty,
            MaxResults = maxResults,
            IncludeResponse = includeResponse,
        }, cancellationToken);
    }

    private static Task<WorkspaceQueryResult<BridgeOperationStatus>> Failure(string diagnostic)
    {
        return Task.FromResult(new WorkspaceQueryResult<BridgeOperationStatus>
        {
            IsPartial = true,
            Diagnostics = new[] { diagnostic },
        });
    }
}
