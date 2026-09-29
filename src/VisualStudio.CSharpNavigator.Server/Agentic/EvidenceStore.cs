using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ModelContextProtocol.Protocol;

namespace VisualStudio.CSharpNavigator.Server.Agentic;

public sealed class EvidenceStore
{
    private static readonly TimeSpan DefaultTimeToLive = TimeSpan.FromMinutes(30);
    private const int MaxResourceCount = 512;
    private const int MaxExpiredUriCount = 2048;
    private const int MaxResourceCharacters = 500_000;
    private const long MaxTotalCharacters = 8_000_000;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly ConcurrentDictionary<string, EvidenceResourceEntry> _resources = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, EvidenceResourceAlias> _aliases = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> _expiredUris = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _contentIndex = new(StringComparer.Ordinal);

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
        RemoveExpired();
        var expires = now.Add(timeToLive ?? DefaultTimeToLive);
        var contentHash = ComputeContentHash(mimeType, text);
        if (_contentIndex.TryGetValue(contentHash, out var canonicalUri)
            && _resources.TryGetValue(canonicalUri, out var canonical)
            && canonical.ExpiresUtc > now)
        {
            _aliases[uri] = new EvidenceResourceAlias(canonicalUri, now, expires, isPartial || canonical.IsPartial);
            _expiredUris.TryRemove(uri, out _);
            return;
        }

        var wasTruncated = text.Length > MaxResourceCharacters;
        if (wasTruncated)
        {
            text = text[..MaxResourceCharacters];
            text += Environment.NewLine + "[EvidenceTruncated: resource exceeded the per-resource character budget; use a narrower query.]";
            isPartial = true;
        }

        _resources[uri] = new EvidenceResourceEntry(
            uri,
            name,
            title,
            description,
            mimeType,
            text,
            isPartial,
            now,
            expires,
            contentHash);
        _contentIndex[contentHash] = uri;
        _aliases.TryRemove(uri, out _);
        _expiredUris.TryRemove(uri, out _);
        EnforceCapacity(now);
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
        var (baseUri, offset, maxCharacters) = ParseReadRange(uri);
        if (!_resources.TryGetValue(baseUri, out var resource)
            && _aliases.TryGetValue(baseUri, out var alias)
            && alias.ExpiresUtc > DateTimeOffset.UtcNow)
        {
            _resources.TryGetValue(alias.CanonicalUri, out resource);
        }

        if (resource is null)
        {
            return new ReadResourceResult
            {
                Contents = new ResourceContents[]
                {
                    new TextResourceContents
                    {
                        Uri = uri,
                        MimeType = "text/plain",
                        Text = JsonSerializer.Serialize(new
                        {
                            code = _expiredUris.ContainsKey(baseUri) ? "EvidenceResourceExpired" : "EvidenceResourceNotFound",
                            uri,
                            message = _expiredUris.ContainsKey(baseUri)
                                ? "Evidence resource expired or was evicted; rerun the producing workflow."
                                : "Evidence resource was not found in this MCP server process.",
                        }),
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
                    Uri = uri,
                    MimeType = resource.MimeType,
                    Text = ReadRange(resource.Text, offset, maxCharacters),
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
                _expiredUris[pair.Key] = 0;
                _contentIndex.TryRemove(pair.Value.ContentHash, out _);
            }
        }

        foreach (var pair in _aliases)
        {
            if (pair.Value.ExpiresUtc <= now)
            {
                _aliases.TryRemove(pair.Key, out _);
                _expiredUris[pair.Key] = 0;
            }
        }

        EnforceExpiredUriCapacity();
    }

    private void EnforceCapacity(DateTimeOffset now)
    {
        RemoveExpired();
        while (_resources.Count > MaxResourceCount || GetTotalCharacters() > MaxTotalCharacters)
        {
            var oldest = _resources.Values
                .OrderBy(resource => resource.CreatedUtc)
                .FirstOrDefault();
            if (oldest is null)
            {
                break;
            }

            _resources.TryRemove(oldest.Uri, out _);
            _expiredUris[oldest.Uri] = 0;
            _contentIndex.TryRemove(oldest.ContentHash, out _);
        }

        EnforceExpiredUriCapacity();
    }

    private long GetTotalCharacters()
    {
        return _resources.Values.Sum(resource => (long)resource.Text.Length);
    }

    private void EnforceExpiredUriCapacity()
    {
        while (_expiredUris.Count > MaxExpiredUriCount)
        {
            var staleUri = _expiredUris.Keys
                .OrderBy(uri => uri, StringComparer.Ordinal)
                .FirstOrDefault();
            if (staleUri is null)
            {
                break;
            }

            _expiredUris.TryRemove(staleUri, out _);
        }
    }

    private static (string BaseUri, int Offset, int? MaxCharacters) ParseReadRange(string uri)
    {
        var questionMark = uri.IndexOf('?');
        if (questionMark < 0)
        {
            return (uri, 0, null);
        }

        var baseUri = uri[..questionMark];
        var offset = 0;
        int? maxCharacters = null;
        foreach (var part in uri[(questionMark + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = part.IndexOf('=');
            var key = separator < 0 ? part : part[..separator];
            var value = separator < 0 ? string.Empty : part[(separator + 1)..];
            if (!int.TryParse(value, out var parsed))
            {
                continue;
            }

            if (string.Equals(key, "offset", StringComparison.OrdinalIgnoreCase))
            {
                offset = Math.Max(0, parsed);
            }
            else if (string.Equals(key, "maxChars", StringComparison.OrdinalIgnoreCase))
            {
                maxCharacters = Math.Max(1, parsed);
            }
        }

        return (baseUri, offset, maxCharacters);
    }

    private static string ReadRange(string text, int offset, int? maxCharacters)
    {
        if (offset >= text.Length)
        {
            return string.Empty;
        }

        var length = maxCharacters.HasValue
            ? Math.Min(maxCharacters.Value, text.Length - offset)
            : text.Length - offset;
        return text.Substring(offset, length);
    }

    private static string ComputeContentHash(string mimeType, string text)
    {
        using var hash = SHA256.Create();
        return Convert.ToHexString(hash.ComputeHash(Encoding.UTF8.GetBytes(mimeType + "\n" + text)));
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
        DateTimeOffset ExpiresUtc,
        string ContentHash);

    private sealed record EvidenceResourceAlias(
        string CanonicalUri,
        DateTimeOffset CreatedUtc,
        DateTimeOffset ExpiresUtc,
        bool IsPartial);
}
