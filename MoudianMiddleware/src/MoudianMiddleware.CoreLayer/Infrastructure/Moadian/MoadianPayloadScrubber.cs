using System.Text.Json;
using System.Text.Json.Nodes;

namespace MoudianMiddleware.CoreLayer.Infrastructure.Moadian;

/// <summary>Fail-closed normalization for optional Request/Response JSON in operational audit records.</summary>
public interface IMoadianPayloadScrubber
{
    /// <summary>Returns JSON with only explicitly approved operational scalar fields preserved.</summary>
    string ScrubJson(string? json);
}

/// <summary>
/// Redacts all payload fields except a small allowlist. This deliberately avoids trying to recognize
/// every PII/credential field in evolving tax-service payloads.
/// </summary>
public sealed class MoadianPayloadScrubber : IMoadianPayloadScrubber
{
    private const string Redacted = "[REDACTED]";
    private static readonly HashSet<string> _safeScalarFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "uid", "referencenumber", "status", "packettype", "errorcode", "success", "durationms", "correlationid", "action"
    };

    /// <inheritdoc />
    public string ScrubJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return "{\"redacted\":true,\"reason\":\"empty payload\"}";
        }

        try
        {
            var node = JsonNode.Parse(json);
            return ScrubNode(node, null)?.ToJsonString() ?? "null";
        }
        catch (JsonException)
        {
            // Never persist an unparseable/raw SDK body as a fallback.
            return "{\"redacted\":true,\"reason\":\"non-JSON payload\"}";
        }
    }

    private static JsonNode? ScrubNode(JsonNode? node, string? propertyName)
    {
        if (node is null)
        {
            return null;
        }

        if (propertyName is not null && !_safeScalarFields.Contains(propertyName))
        {
            return JsonValue.Create(Redacted);
        }

        if (node is JsonObject jsonObject)
        {
            var result = new JsonObject();
            foreach (var property in jsonObject)
            {
                result[property.Key] = ScrubNode(property.Value, property.Key);
            }
            return result;
        }

        if (node is JsonArray jsonArray)
        {
            var result = new JsonArray();
            foreach (var item in jsonArray)
            {
                result.Add(ScrubNode(item, null));
            }
            return result;
        }

        return propertyName is not null && _safeScalarFields.Contains(propertyName)
            ? node.DeepClone()
            : JsonValue.Create(Redacted);
    }
}
