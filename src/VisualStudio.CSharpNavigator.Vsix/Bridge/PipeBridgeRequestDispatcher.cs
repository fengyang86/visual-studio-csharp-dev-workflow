using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VisualStudio.CSharpNavigator.Protocol;

namespace VisualStudio.CSharpNavigator.Vsix.Bridge;

// 与 VS API 无关，真实宿主和隔离管道测试共用同一套取消及结果保留逻辑。
internal sealed class PipeBridgeRequestDispatcher
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _active = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Entry> _operations = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTimeOffset> _earlyCancellations = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _mutationGate = new(1, 1);
    private readonly Func<PipeBridgeRequestEnvelope, CancellationToken, Task<string>> _dispatch;
    private readonly Func<PipeBridgeRequestEnvelope, CancellationToken, Task<string?>> _validateTarget;
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly string _pipeName;
    private readonly int _capacity;
    private readonly int _maxResponseCharacters;
    private readonly TimeSpan _retention;

    public PipeBridgeRequestDispatcher(
        string pipeName,
        Func<PipeBridgeRequestEnvelope, CancellationToken, Task<string>> dispatch,
        Func<PipeBridgeRequestEnvelope, CancellationToken, Task<string?>> validateTarget,
        int capacity = 256,
        int maxResponseCharacters = 32768,
        TimeSpan? retention = null,
        Func<DateTimeOffset>? utcNow = null)
    {
        if (capacity < 1 || maxResponseCharacters < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        _pipeName = pipeName;
        _dispatch = dispatch;
        _validateTarget = validateTarget;
        _capacity = capacity;
        _maxResponseCharacters = maxResponseCharacters;
        _retention = retention ?? TimeSpan.FromMinutes(30);
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    }

    public async Task<string> ExecuteAsync(string requestJson, CancellationToken cancellationToken)
    {
        PipeBridgeRequestEnvelope request;
        try
        {
            request = JsonSerializer.Deserialize<PipeBridgeRequestEnvelope>(requestJson, PipeBridgeProtocol.JsonOptions)
                ?? throw new JsonException("The bridge request body was empty.");
        }
        catch (JsonException ex)
        {
            return Error("MalformedRequest", ex.Message);
        }

        if (!string.Equals(request.ProtocolVersion, PipeBridgeProtocol.Version, StringComparison.Ordinal))
        {
            return Error("UnsupportedProtocolVersion", "The bridge protocol version does not match.");
        }

        if (request.RequestId?.Length > 128)
        {
            return Error("InvalidRequestId", "The request id must not exceed 128 characters.");
        }

        if (request.Method == "CancelRequest" || request.Method == "GetOperationStatus")
        {
            try
            {
                var targetError = await _validateTarget(request, cancellationToken).ConfigureAwait(false);
                if (targetError is not null)
                {
                    return targetError;
                }

                return request.Method == "CancelRequest"
                    ? Cancel(Deserialize<CancelPipeBridgeRequest>(request).RequestId)
                    : Query(Deserialize<BridgeOperationStatusRequest>(request));
            }
            catch (JsonException ex)
            {
                return Error("MalformedRequest", ex.Message);
            }
        }

        var requestId = string.IsNullOrWhiteSpace(request.RequestId) ? Guid.NewGuid().ToString("N") : request.RequestId!;
        var tracked = BridgeOperationPolicy.RequiresTracking(request.Method);
        var serializeMutation = BridgeOperationPolicy.RequiresWorkspaceMutationSerialization(request.Method);
        using var source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var entry = new Entry(requestId, request.Method, Fingerprint(request), source, _utcNow());
        var canceledBeforeStart = false;
        lock (_gate)
        {
            Prune();
            if (_active.TryGetValue(requestId, out var active))
            {
                return Duplicate(active, entry);
            }

            if (_operations.TryGetValue(requestId, out var previous))
            {
                return Duplicate(previous, entry);
            }

            if (_active.Count >= _capacity || (tracked && !MakeRoom()))
            {
                return Error("BridgeRequestCapacityExceeded", "The bridge request or operation registry is full; this request was not executed.");
            }

            canceledBeforeStart = _earlyCancellations.Remove(requestId);
            _active.Add(requestId, entry);
            if (tracked)
            {
                _operations.Add(requestId, entry);
            }
        }

        string response;
        var state = "Completed";
        var dispatchStarted = false;
        try
        {
            if (canceledBeforeStart)
            {
                source.Cancel();
            }

            source.Token.ThrowIfCancellationRequested();
            var targetError = await _validateTarget(request, source.Token).ConfigureAwait(false);
            if (targetError is not null)
            {
                response = targetError;
                state = "Rejected";
            }
            else
            {
                source.Token.ThrowIfCancellationRequested();
                if (serializeMutation)
                {
                    // Workspace mutations must not interleave (see
                    // BridgeOperationPolicy.RequiresWorkspaceMutationSerialization).
                    // dispatchStarted is set only after the gate is acquired so a
                    // cancel while waiting is still reported as "never dispatched".
                    await _mutationGate.WaitAsync(source.Token).ConfigureAwait(false);
                    try
                    {
                        dispatchStarted = true;
                        response = await _dispatch(request, source.Token).ConfigureAwait(false);
                    }
                    finally
                    {
                        _mutationGate.Release();
                    }
                }
                else
                {
                    dispatchStarted = true;
                    response = await _dispatch(request, source.Token).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            state = dispatchStarted ? "OutcomeUnknown" : "CanceledBeforeStart";
            response = Error(dispatchStarted ? tracked ? "OperationOutcomeUnknown" : "RequestCanceled" : "RequestCanceledBeforeStart",
                dispatchStarted && tracked
                    ? "The request was canceled or timed out during execution; the side effect may still have occurred. Query the operation record and verify current state; do not retry blindly."
                    : dispatchStarted ? "The read-only query was canceled or timed out." : "The request was canceled before dispatch; the target operation was not invoked.");
        }
        catch (Exception ex)
        {
            state = dispatchStarted ? "OutcomeUnknown" : "Rejected";
            response = Error(dispatchStarted && tracked ? "OperationOutcomeUnknown" : "BridgeRequestRejected", ex.Message);
        }
        // 先完成记录再写响应；不能让断线抹掉已经执行的结果。
        lock (_gate)
        {
            entry.State = state;
            entry.CompletedAtUtc = _utcNow();
            entry.CancellationRequested |= source.IsCancellationRequested;
            entry.Response = response.Length <= _maxResponseCharacters ? response : null;
            _active.Remove(requestId);
        }

        return tracked ? WithReceipt(response, requestId) : response;
    }

    private string Cancel(string requestId)
    {
        if (string.IsNullOrWhiteSpace(requestId) || requestId.Length > 128)
        {
            return Error("InvalidRequestId", "CancelRequest requires a valid request id.");
        }

        Entry? entry;
        lock (_gate)
        {
            Prune();
            if (!_active.TryGetValue(requestId, out entry))
            {
                if (_operations.ContainsKey(requestId))
                {
                    return Success(new WorkspaceQueryResult<object> { Diagnostics = new[] { "BridgeRequestAlreadyTerminal" } });
                }

                if (_earlyCancellations.Count >= _capacity)
                {
                    return Error("BridgeCancellationCapacityExceeded", "待处理取消记录已满，未确认取消。");
                }

                _earlyCancellations[requestId] = _utcNow().AddMinutes(1);
            }
            else
            {
                entry.CancellationRequested = true;
            }
        }

        if (entry is not null)
        {
            try
            {
                entry.Source.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // 结束与取消并发时，已经完成的旧请求不需要再次取消。
            }
        }

        return Success(new WorkspaceQueryResult<object> { Diagnostics = new[] { "BridgeRequestCancellationAccepted" } });
    }

    private string Query(BridgeOperationStatusRequest request)
    {
        if (request.MaxResults < 1 || request.MaxResults > 20 || request.RequestId?.Length > 128
            || (request.IncludeResponse && string.IsNullOrWhiteSpace(request.RequestId)))
        {
            return Error("InvalidOperationStatusRequest", "查询上限必须为 1-20，The request id must not exceed 128 characters.");
        }

        lock (_gate)
        {
            Prune();
            var matching = _operations.Values
                .Where(entry => (string.IsNullOrWhiteSpace(request.RequestId) || entry.RequestId == request.RequestId)
                    && (string.IsNullOrWhiteSpace(request.Method) || entry.Method == request.Method))
                .OrderByDescending(entry => entry.StartedAtUtc)
                .ToArray();
            var items = matching.Take(request.MaxResults).Select(entry => new BridgeOperationStatus
            {
                RequestId = entry.RequestId,
                Method = entry.Method,
                PipeName = _pipeName,
                State = entry.State,
                StartedAtUtc = entry.StartedAtUtc,
                CompletedAtUtc = entry.CompletedAtUtc,
                ExpiresAtUtc = entry.CompletedAtUtc?.Add(_retention),
                CancellationRequested = entry.CancellationRequested,
                IsTerminal = entry.CompletedAtUtc.HasValue,
                ResponseOmitted = entry.CompletedAtUtc.HasValue && entry.Response is null,
                ResponseJson = request.IncludeResponse ? entry.Response : null,
            }).ToArray();
            return Success(new WorkspaceQueryResult<BridgeOperationStatus>
            {
                Items = items,
                IsPartial = matching.Length > items.Length || items.Any(item => item.State == "OutcomeUnknown" || item.ResponseOmitted)
                    || (!string.IsNullOrWhiteSpace(request.RequestId) && items.Length == 0),
                Diagnostics = new[]
                {
                    items.Length == 0 ? "OperationNotFoundOrExpired" : "BridgeOperationStatusAvailable",
                    "Operation records are retained only within the current VSIX process for a limited time; not-found or expired does not mean the operation did not execute. Completed means a response was returned, not that the mutation succeeded; inspect the original response.",
                },
            });
        }
    }

    private string Duplicate(Entry previous, Entry incoming)
    {
        if (previous.Fingerprint != incoming.Fingerprint)
        {
            return Error("BridgeRequestIdConflict", "The same request id cannot be reused for a different operation or payload.");
        }

        if (!previous.CompletedAtUtc.HasValue)
        {
            return Error("BridgeRequestAlreadyRunning", "The request is still running and will not be re-executed. Query the operation record.");
        }

        return previous.Response is null
            ? Error("BridgeOperationResponseOmitted", "操作已执行但原始响应超过保留上限，不会重复执行。")
            : WithReceipt(previous.Response, previous.RequestId);
    }

    private void Prune()
    {
        var now = _utcNow();
        foreach (var key in _earlyCancellations.Where(pair => pair.Value <= now).Select(pair => pair.Key).ToArray())
        {
            _earlyCancellations.Remove(key);
        }

        foreach (var key in _operations.Where(pair => pair.Value.CompletedAtUtc?.Add(_retention) <= now).Select(pair => pair.Key).ToArray())
        {
            _operations.Remove(key);
        }
    }

    private bool MakeRoom()
    {
        if (_operations.Count < _capacity)
        {
            return true;
        }

        var oldest = _operations.Values.Where(entry => entry.CompletedAtUtc.HasValue)
            .OrderBy(entry => entry.CompletedAtUtc).FirstOrDefault();
        return oldest is not null && _operations.Remove(oldest.RequestId);
    }

    private static string Fingerprint(PipeBridgeRequestEnvelope request)
    {
        using var hash = SHA256.Create();
        var payload = request.Payload.ValueKind == JsonValueKind.Undefined ? string.Empty : request.Payload.GetRawText();
        return Convert.ToBase64String(hash.ComputeHash(Encoding.UTF8.GetBytes(request.Method + "\n" + payload)));
    }

    private static T Deserialize<T>(PipeBridgeRequestEnvelope request) where T : new()
    {
        return request.Payload.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
            ? new T()
            : request.Payload.Deserialize<T>(PipeBridgeProtocol.JsonOptions) ?? new T();
    }

    private static string Success<T>(WorkspaceQueryResult<T> result)
    {
        return JsonSerializer.Serialize(new PipeBridgeResponseEnvelope<T> { Result = result }, PipeBridgeProtocol.JsonOptions);
    }

    private static string Error(string code, string message)
    {
        return JsonSerializer.Serialize(new PipeBridgeErrorResponseEnvelope
        {
            Error = new PipeBridgeError { Code = code, Message = message },
        }, PipeBridgeProtocol.JsonOptions);
    }

    private static string WithReceipt(string response, string requestId)
    {
        using var document = JsonDocument.Parse(response);
        var properties = document.RootElement.EnumerateObject().ToDictionary(property => property.Name, property => (object)property.Value);
        properties["operationId"] = requestId;
        return JsonSerializer.Serialize(properties, PipeBridgeProtocol.JsonOptions);
    }

    private sealed class Entry
    {
        public Entry(string requestId, string method, string fingerprint, CancellationTokenSource source, DateTimeOffset startedAtUtc)
        {
            RequestId = requestId;
            Method = method;
            Fingerprint = fingerprint;
            Source = source;
            StartedAtUtc = startedAtUtc;
        }

        public string RequestId { get; }
        public string Method { get; }
        public string Fingerprint { get; }
        public CancellationTokenSource Source { get; }
        public DateTimeOffset StartedAtUtc { get; }
        public DateTimeOffset? CompletedAtUtc { get; set; }
        public string State { get; set; } = "Running";
        public bool CancellationRequested { get; set; }
        public string? Response { get; set; }
    }
}
