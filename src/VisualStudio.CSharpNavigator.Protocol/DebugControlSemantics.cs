namespace VisualStudio.CSharpNavigator.Protocol;

public static class DebugControlSemantics
{
    public const string AlreadySatisfiedDiagnostic =
        "Debug Control did not mutate Visual Studio debugger state because the requested state was already active.";

    public static bool TryGetAlreadySatisfiedMessage(
        DebugControlAction action,
        DebugSessionStatus status,
        out string message)
    {
        if (action == DebugControlAction.Stop && status.State == DebuggerState.Design)
        {
            message = "Stop debugging skipped because Visual Studio is already in design mode.";
            return true;
        }

        message = string.Empty;
        return false;
    }
}
