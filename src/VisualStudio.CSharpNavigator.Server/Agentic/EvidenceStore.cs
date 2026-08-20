using System.Collections.Concurrent;
using System.Text.Json;
using ModelContextProtocol.Protocol;

namespace VisualStudio.CSharpNavigator.Server.Agentic;

public sealed class EvidenceStore
{
    private static readonly TimeSpan DefaultTimeToLive = TimeSpan.FromMinutes(30);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly ConcurrentDictionary<string, EvidenceResourceEntry> _resources = new(StringComparer.Ordinal);

    public void AddTextResource(
        string uri,
        string name,
        string title,
        string description,
        string mimeType,
        string text,
        bool isPartial,
        TimeSpan? timeToLive = null)
    {
        var now = DateTimeOffset.UtcNow;
        _resources[uri] = new EvidenceResourceEntry(
            uri,
            name,
            title,
            description,
            mimeType,
            text,
            isPartial,
            now,
            now.Add(timeToLive ?? DefaultTimeToLive));
    }

    public void AddJsonResource(
        string uri,
        string name,
        string title,
        string description,
        object payload,
        bool isPartial,
        TimeSpan? timeToLive = null)
    {
        AddTextResource(
            uri,
            name,
            title,
            description,
            "application/json",
            JsonSerializer.Serialize(payload, JsonOptions),
            isPartial,
            timeToLive);
    }

    public Resource[] ListResources()
    {
        RemoveExpired();
        return _resources.Values
            .OrderByDescending(resource => resource.CreatedUtc)
            .Select(resource => new Resource
            {
                Uri = resource.Uri,
                Name = resource.Name,
                Title = resource.Title,
                Description = resource.Description,
                MimeType = resource.MimeType,
                Size = resource.Text.Length,
            })
            .ToArray();
    }

    public ReadResourceResult ReadResource(string uri)
    {
        RemoveExpired();
        if (!_resources.TryGetValue(uri, out var resource))
        {
            return new ReadResourceResult
            {
                Contents = new ResourceContents[]
                {
                    new TextResourceContents
                    {
                        Uri = uri,
                        MimeType = "text/plain",
                        Text = $"Evidence resource not found or expired: {uri}",
                    },
                },
            };
        }

        return new ReadResourceResult
        {
            Contents = new ResourceContents[]
            {
                new TextResourceContents
                {
                    Uri = resource.Uri,
                    MimeType = resource.MimeType,
                    Text = resource.Text,
                },
            },
        };
    }

    private void RemoveExpired()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var pair in _resources)
        {
            if (pair.Value.ExpiresUtc <= now)
            {
                _resources.TryRemove(pair.Key, out _);
            }
        }
    }

    private sealed record EvidenceResourceEntry(
        string Uri,
        string Name,
        string Title,
        string Description,
        string MimeType,
        string Text,
        bool IsPartial,
        DateTimeOffset CreatedUtc,
        DateTimeOffset ExpiresUtc);
}
