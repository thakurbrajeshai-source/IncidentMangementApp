using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace IncidentManagement.Api.Services;

/// <summary>
/// Resolves workflow step placeholders. Syntax:
///   {{input.fieldName}}            -> value supplied by the runner for that input
///   {{stepN.response.field.path}}  -> a field out of step N's earlier response
/// Step numbers are the 1-based StepOrder. References must point to earlier steps.
/// </summary>
public static partial class WorkflowPlaceholderResolver
{
    [GeneratedRegex(@"\{\{\s*([^{}]+?)\s*\}\}", RegexOptions.Compiled)]
    private static partial Regex PlaceholderRegex();

    private static readonly Regex StepRef = new(@"^step(\d+)\.response\.(.+)$", RegexOptions.Compiled);

    /// <summary>Textual resolution for URLs and header values. Missing keys throw.</summary>
    public static string ResolveText(string template, Func<string, JsonNode?> lookup)
        => PlaceholderRegex().Replace(template, m =>
        {
            var key = m.Groups[1].Value.Trim();
            var val = lookup(key) ?? throw new InvalidOperationException($"Placeholder '{{{{{key}}}}}' could not be resolved.");
            return ScalarToString(val);
        });

    /// <summary>
    /// Resolves placeholders inside a JSON body template. A whole-string placeholder whose
    /// value is an object/array is spliced in as a node; otherwise values become properly
    /// JSON-escaped strings. Returns null for an empty template.
    /// </summary>
    public static JsonNode? ResolveBody(string bodyTemplate, Func<string, JsonNode?> lookup)
    {
        if (string.IsNullOrWhiteSpace(bodyTemplate)) return null;

        JsonNode? root;
        try { root = JsonNode.Parse(bodyTemplate); }
        catch (JsonException)
        {
            throw new InvalidOperationException("Workflow step body_template must be valid JSON.");
        }

        WalkReplace(root, lookup);
        return root;
    }

    /// <summary>Builds the lookup function for a run: user inputs + prior step responses.</summary>
    public static Func<string, JsonNode?> Lookup(Dictionary<string, JsonNode?> inputs, Dictionary<int, JsonNode?> responses)
        => key =>
        {
            if (key.StartsWith("input.", StringComparison.Ordinal))
            {
                var field = key["input.".Length..];
                if (!inputs.TryGetValue(field, out var v))
                    throw new InvalidOperationException($"Unknown workflow input '{field}'.");
                return v;
            }

            var m = StepRef.Match(key);
            if (m.Success)
            {
                var order = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                var path = m.Groups[2].Value;
                if (!responses.TryGetValue(order, out var resp))
                    throw new InvalidOperationException(
                        $"Step {order} hasn't run yet — placeholders can only reference earlier steps.");
                return PathLookup(resp, order, path);
            }

            throw new InvalidOperationException($"Unknown placeholder key '{key}'.");
        };

    private static JsonNode? PathLookup(JsonNode? node, int stepOrder, string path)
    {
        var current = node;
        foreach (var segment in path.Split('.'))
        {
            switch (current)
            {
                case JsonObject o when o.TryGetPropertyValue(segment, out var next):
                    current = next;
                    break;
                case JsonArray arr when int.TryParse(segment, NumberStyles.None, CultureInfo.InvariantCulture, out var idx)
                                       && idx >= 0 && idx < arr.Count:
                    current = arr[idx];
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Path '{path}' not found in step {stepOrder} response.");
            }
        }
        return current;
    }

    private static void WalkReplace(JsonNode? node, Func<string, JsonNode?> lookup)
    {
        switch (node)
        {
            case JsonObject o:
                foreach (var key in o.Select(kv => kv.Key).ToList())
                {
                    var child = o[key];
                    if (child is JsonValue)
                    {
                        var replacement = ResolveStringValue(child.GetValue<string>() ?? "", lookup);
                        if (replacement.Node is not null) o[key] = replacement.Node;
                        else if (replacement.Text is not null) o[key] = replacement.Text;
                    }
                    else WalkReplace(child, lookup);
                }
                break;
            case JsonArray arr:
                for (var i = 0; i < arr.Count; i++)
                {
                    var child = arr[i];
                    if (child is JsonValue)
                    {
                        var replacement = ResolveStringValue(child.GetValue<string>() ?? "", lookup);
                        if (replacement.Node is not null) arr[i] = replacement.Node;
                        else if (replacement.Text is not null) arr[i] = replacement.Text;
                    }
                    else WalkReplace(child, lookup);
                }
                break;
        }
    }

    private static (string? Text, JsonNode? Node) ResolveStringValue(string s, Func<string, JsonNode?> lookup)
    {
        var matches = PlaceholderRegex().Matches(s);

        // Whole string is a single placeholder -> splice the resolved value in as a node,
        // which guarantees correct JSON escaping and supports objects/arrays.
        if (matches.Count == 1 && matches[0].Index == 0 && matches[0].Length == s.Length)
        {
            var val = lookup(matches[0].Groups[1].Value.Trim());
            if (val is null) return (null, JsonValue.Create(""));
            return (null, val);
        }

        var sb = new StringBuilder();
        var idx = 0;
        foreach (Match m in matches)
        {
            sb.Append(s, idx, m.Index - idx);
            var val = lookup(m.Groups[1].Value.Trim());
            sb.Append(ScalarToString(val));
            idx = m.Index + m.Length;
        }
        sb.Append(s, idx, s.Length - idx);
        return (sb.ToString(), null);
    }

    private static string ScalarToString(JsonNode? node) => node switch
    {
        JsonValue v when v.TryGetValue<string>(out var s) => s,
        JsonValue v when v.TryGetValue<decimal>(out var d) => d.ToString(CultureInfo.InvariantCulture),
        JsonValue v when v.TryGetValue<bool>(out var b) => b ? "true" : "false",
        null => "",
        _ => node.ToJsonString(),
    };
}
