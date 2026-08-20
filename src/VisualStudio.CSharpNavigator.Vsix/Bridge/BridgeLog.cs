using System;
using Microsoft.VisualStudio.Shell;

namespace VisualStudio.CSharpNavigator.Vsix.Bridge;

internal static class BridgeLog
{
    private const string Source = "VisualStudio CSharpNavigator";

    public static void Info(string message)
    {
        ActivityLog.LogInformation(Source, message);
    }

    public static void Warning(string message)
    {
        ActivityLog.LogWarning(Source, message);
    }

    public static void Error(string message, Exception? exception = null)
    {
        ActivityLog.LogError(Source, exception is null ? message : message + Environment.NewLine + exception);
    }
}
