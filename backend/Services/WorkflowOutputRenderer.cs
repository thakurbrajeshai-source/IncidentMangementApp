using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace IncidentManagement.Api.Services;

/// <summary>
/// Universal output rule (PRD section 6a — non-negotiable): no role, including
/// Admin, ever sees a raw JSON/XML response. This single renderer converts any
/// API response into a table:
///   - object          -> 2-column Field/Value table (nested flattened with dot-notation keys)
///   - array of objects-> multi-column table, one row per item, columns = union of keys
///   - scalar / array of scalars / non-JSON text -> single "Value" column
/// Every consumer of a workflow step's output goes through here.
/// </summary>
public static class WorkflowOutputRenderer
{
    /// <summary>A rendered table: columns in display order + rows keyed by column.</summary>
    public record RenderedTable(List<string> Columns, List<Dictionary<string, object?>> Rows);

    public static RenderedTable Render(string rawResponse)
    {
        if (string.IsNullOrWhiteSpace(rawResponse))
            return new RenderedTable(["Value"], [new Dictionary<string, object?> { ["Value"] = "(empty response)" }]);

        JsonNode? node;
        try { node = JsonNode.Parse(rawResponse); }
        catch (JsonException)
        {
            // Not JSON (plain text / XML / HTML) — present it as a single cell so it is
            // still readable but never rendered as a raw blob inside a JSON dump.
            var text = rawResponse.Length > 2000 ? rawResponse[..2000] + "…" : rawResponse;
            return new RenderedTable(["Value"], [new Dictionary<string, object?> { ["Value"] = text }]);
        }

        return node switch
        {
            JsonArray arr => RenderArray(arr),
            JsonObject obj => RenderObject(obj),
            JsonValue v => RenderScalar(v),
            _ => RenderScalar(node),
        };
    }

    private static RenderedTable RenderArray(JsonArray arr)
    {
        // Array of objects -> one row per item, columns = union of flattened keys.
        var flattened = arr
            .Select(item => item is JsonObject or JsonArray ? Flatten(item) : new Dictionary<string, object?> { ["value"] = ToScalar(item) })
            .ToList();

        if (flattened.Count == 0)
            return new RenderedTable(["Value"], [new Dictionary<string, object?> { ["Value"] = "(empty array)" }]);

        var columns = new List<string>();
        var seen = new HashSet<string>();
        foreach (var row in flattened)
            foreach (var key in row.Keys)
                if (seen.Add(key)) columns.Add(key);

        // Normalize: every row gets every column (missing = null).
        var rows = flattened.Select(r => {
            var d = new Dictionary<string, object?>();
            foreach (var c in columns) d[c] = r.TryGetValue(c, out var v) ? v : null;
            return d;
        }).ToList();

        return new RenderedTable(columns, rows);
    }

    private static RenderedTable RenderObject(JsonObject obj)
    {
        // Object -> 2-column Field/Value table; nested objects flattened with dot-notation.
        var flat = Flatten(obj);
        var rows = flat
            .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .Select(kv => new Dictionary<string, object?>
            {
                ["Field"] = kv.Key,
                ["Value"] = kv.Value,
            })
            .ToList();
        if (rows.Count == 0) rows.Add(new Dictionary<string, object?> { ["Field"] = "(no fields)", ["Value"] = "" });
        return new RenderedTable(["Field", "Value"], rows);
    }

    private static RenderedTable RenderScalar(JsonNode? node)
        => new(["Value"], [new Dictionary<string, object?> { ["Value"] = ToScalar(node) }]);

    /// <summary>Recursively flattens a node into dot-notation keys (arrays use index paths
    /// like "items.0.name"). Scalar leaves are converted to display values.</summary>
    private static Dictionary<string, object?> Flatten(JsonNode? node)
    {
        var result = new Dictionary<string, object?>();
        FlattenInto("", node, result);
        return result;
    }

    private static void FlattenInto(string prefix, JsonNode? node, Dictionary<string, object?> target)
    {
        switch (node)
        {
            case JsonObject o:
                foreach (var (key, val) in o)
                    FlattenInto(Join(prefix, key), val, target);
                break;
            case JsonArray arr:
                for (var i = 0; i < arr.Count; i++)
                    FlattenInto(Join(prefix, i.ToString(CultureInfo.InvariantCulture)), arr[i], target);
                break;
            case null:
                target[prefix] = null;
                break;
            default:
                target[prefix] = ToScalar(node);
                break;
        }
    }

    private static string Join(string prefix, string key)
        => string.IsNullOrEmpty(prefix) ? key : $"{prefix}.{key}";

    private static object? ToScalar(JsonNode? node) => node switch
    {
        JsonValue v when v.TryGetValue<string>(out var s) => s,
        JsonValue v when v.TryGetValue<decimal>(out var d) => d,
        JsonValue v when v.TryGetValue<bool>(out var b) => b,
        null => null,
        _ => node.ToJsonString(), // nested object/array that escaped flattening
    };
}
