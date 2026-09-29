using System.Threading;
using System.Threading.Tasks;
using VisualStudio.CSharpNavigator.Protocol;

namespace VisualStudio.CSharpNavigator.Abstractions;

public interface IVisualStudioOperationBridge
{
    Task<WorkspaceQueryResult<BridgeOperationStatus>> GetOperationStatusAsync(
        BridgeOperationStatusRequest request,
        CancellationToken cancellationToken);
}
