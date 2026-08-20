using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using VisualStudio.CSharpNavigator.Vsix.Bridge;
using VisualStudio.CSharpNavigator.Vsix.Workspace;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Task = System.Threading.Tasks.Task;

namespace VisualStudio.CSharpNavigator.Vsix;

[PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
[ProvideAutoLoad(VSConstants.UICONTEXT.NoSolution_string, PackageAutoLoadFlags.BackgroundLoad)]
[ProvideAutoLoad(VSConstants.UICONTEXT.SolutionExists_string, PackageAutoLoadFlags.BackgroundLoad)]
[Guid(PackageGuidString)]
public sealed class CodeNavigatorPackage : AsyncPackage
{
    public const string PackageGuidString = "1c2e2f5d-d9db-42e2-9029-107d9c90f1da";

    private BridgeInstanceRegistry? _registry;
    private VisualStudioBridgeServer? _bridgeServer;

    protected override async Task InitializeAsync(
        CancellationToken cancellationToken,
        IProgress<ServiceProgressData> progress)
    {
        await base.InitializeAsync(cancellationToken, progress).ConfigureAwait(false);

        BridgeLog.Info("Visual Studio C# Dev Workflow bridge package initializing.");

        var queryService = new VisualStudioWorkspaceQueryService(this);
        var debugContextService = new VisualStudioDebugContextService(this);
        var debugControlService = new VisualStudioDebugControlService(this);
        _registry = new BridgeInstanceRegistry(queryService);
        await _registry.InitializeAsync(cancellationToken).ConfigureAwait(false);

        _bridgeServer = new VisualStudioBridgeServer(_registry, queryService, debugContextService, debugControlService);
        _bridgeServer.Start();
        BridgeLog.Info($"Visual Studio C# Dev Workflow bridge started. InstanceId={_registry.InstanceId}; PipeName={_registry.PipeName}.");
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _bridgeServer?.Dispose();
            _registry?.Dispose();
        }

        base.Dispose(disposing);
    }
}
