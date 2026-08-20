using System.Text.Json;
using System.Text.Json.Serialization;

namespace VisualStudio.CSharpNavigator.Vsix.Bridge;

internal static class PipeBridgeProtocol
{
    public const string Version = "1";

    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}

internal sealed class PipeBridgeRequestEnvelope
{
    public string ProtocolVersion { get; set; } = PipeBridgeProtocol.Version;

    public string Method { get; set; } = string.Empty;

    public JsonElement Payload { get; set; }
}

internal sealed class PipeBridgeResponseEnvelope<TItem>
{
    public string ProtocolVersion { get; set; } = PipeBridgeProtocol.Version;

    public VisualStudio.CSharpNavigator.Protocol.WorkspaceQueryResult<TItem>? Result { get; set; }

    public PipeBridgeError? Error { get; set; }

    public long BridgeExecutionMilliseconds { get; set; }
}

internal sealed class PipeBridgeErrorResponseEnvelope
{
    public string ProtocolVersion { get; set; } = PipeBridgeProtocol.Version;

    public PipeBridgeError Error { get; set; } = new();

    public long BridgeExecutionMilliseconds { get; set; }
}

internal sealed class PipeBridgeError
{
    public string Code { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;
}
