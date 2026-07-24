using System.Globalization;
using System.Text.Json.Nodes;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;
using YamlDotNet.Serialization;

namespace Ovi.Sdk.Internal;

/// <summary>
/// Converts between YAML documents and the System.Text.Json node model, so YAML and JSON inputs are
/// deserialized by exactly the same code path (converters, naming policy, validation).
/// </summary>
/// <remarks>
/// This is shared source, compiled independently into each package that needs a YAML→JSON front end
/// (today: <c>Ovi.Sdk.Agents</c> and <c>Ovi.Sdk.Workflows</c>). It stays <see langword="internal"/>
/// so it adds no public surface and creates no cross-package dependency: each assembly gets its own
/// copy, and the one deserialization pipeline is a shared convention, not a shared reference.
/// </remarks>
internal static class YamlJsonBridge
{
    /// <summary>Parses the first YAML document into a <see cref="JsonNode"/> tree; <see langword="null"/> for an empty document.</summary>
    public static JsonNode? ToJsonNode(string yaml)
    {
        var stream = new YamlStream();
        using var reader = new StringReader(yaml);
        stream.Load(reader);

        return stream.Documents.Count == 0 ? null : Convert(stream.Documents[0].RootNode);
    }

    /// <summary>Renders a <see cref="JsonNode"/> tree as YAML.</summary>
    public static string ToYaml(JsonNode node)
    {
        var serializer = new SerializerBuilder().Build();
        return serializer.Serialize(ToPlainObject(node));
    }

    private static JsonNode? Convert(YamlNode node) => node switch
    {
        YamlMappingNode mapping => ConvertMapping(mapping),
        YamlSequenceNode sequence => ConvertSequence(sequence),
        YamlScalarNode scalar => ConvertScalar(scalar),
        _ => throw new NotSupportedException($"YAML node type {node.GetType().Name} is not supported."),
    };

    private static JsonObject ConvertMapping(YamlMappingNode mapping)
    {
        var result = new JsonObject();
        foreach (var (keyNode, valueNode) in mapping.Children)
        {
            if (keyNode is not YamlScalarNode { Value: { } key })
            {
                throw new NotSupportedException("Only scalar YAML mapping keys are supported.");
            }

            result[key] = Convert(valueNode);
        }

        return result;
    }

    private static JsonArray ConvertSequence(YamlSequenceNode sequence)
    {
        var result = new JsonArray();
        foreach (var item in sequence.Children)
        {
            result.Add(Convert(item));
        }

        return result;
    }

    private static JsonNode? ConvertScalar(YamlScalarNode scalar)
    {
        var value = scalar.Value;

        // Quoted and block scalars are always strings; only plain scalars get schema inference.
        if (scalar.Style is not ScalarStyle.Plain and not ScalarStyle.Any)
        {
            return JsonValue.Create(value);
        }

        if (value is null or "" or "~" or "null" or "Null" or "NULL")
        {
            return null;
        }

        if (value is "true" or "True" or "TRUE")
        {
            return JsonValue.Create(true);
        }

        if (value is "false" or "False" or "FALSE")
        {
            return JsonValue.Create(false);
        }

        if (long.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var integer))
        {
            return JsonValue.Create(integer);
        }

        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var floating))
        {
            return JsonValue.Create(floating);
        }

        return JsonValue.Create(value);
    }

    private static object? ToPlainObject(JsonNode? node) => node switch
    {
        null => null,
        JsonObject obj => obj.ToDictionary(property => property.Key, property => ToPlainObject(property.Value)),
        JsonArray array => array.Select(ToPlainObject).ToList(),
        JsonValue value => ScalarToPlainObject(value),
        _ => throw new NotSupportedException($"JSON node type {node.GetType().Name} is not supported."),
    };

    private static object? ScalarToPlainObject(JsonValue value)
    {
        if (value.TryGetValue<bool>(out var boolean))
        {
            return boolean;
        }

        if (value.TryGetValue<long>(out var integer))
        {
            return integer;
        }

        if (value.TryGetValue<double>(out var floating))
        {
            return floating;
        }

        if (value.TryGetValue<string>(out var text))
        {
            return text;
        }

        return value.ToJsonString();
    }
}
