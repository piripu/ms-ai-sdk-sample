using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Ovi.Sdk.Nodes;

namespace Ovi.Sdk.Tools;

/// <summary>
/// A reference to a tool by id, as it appears in agent definitions. In JSON/YAML a reference is
/// either a bare id string (<c>"acme/web-search@2.1.0"</c>) or an object with <c>id</c> and optional
/// <c>options</c> for tool-specific configuration.
/// </summary>
[JsonConverter(typeof(ToolReferenceJsonConverter))]
public sealed record ToolReference
{
    public ToolReference()
    {
    }

    [SetsRequiredMembers]
    public ToolReference(NodeId id, JsonObject? options = null)
    {
        ArgumentNullException.ThrowIfNull(id);
        Id = id;
        Options = options;
    }

    /// <summary>The referenced tool's identity.</summary>
    public required NodeId Id { get; init; }

    /// <summary>Free-form, tool-specific configuration.</summary>
    public JsonObject? Options { get; init; }

    public static implicit operator ToolReference(string id) => new(NodeId.Parse(id));

    public override string ToString() => Id.ToString();
}

/// <summary>Reads a <see cref="ToolReference"/> from either a bare id string or an object form.</summary>
public sealed class ToolReferenceJsonConverter : JsonConverter<ToolReference>
{
    public override ToolReference? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.String:
            {
                var text = reader.GetString()!;
                return NodeId.TryParse(text, out var id)
                    ? new ToolReference(id)
                    : throw new JsonException($"'{text}' is not a valid tool id.");
            }

            case JsonTokenType.StartObject:
            {
                NodeId? id = null;
                JsonObject? toolOptions = null;

                while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                {
                    if (reader.TokenType != JsonTokenType.PropertyName)
                    {
                        throw new JsonException();
                    }

                    var propertyName = reader.GetString();
                    reader.Read();

                    if (string.Equals(propertyName, "id", StringComparison.OrdinalIgnoreCase))
                    {
                        id = JsonSerializer.Deserialize<NodeId>(ref reader, options);
                    }
                    else if (string.Equals(propertyName, "options", StringComparison.OrdinalIgnoreCase))
                    {
                        toolOptions = JsonSerializer.Deserialize<JsonObject>(ref reader, options);
                    }
                    else
                    {
                        reader.Skip();
                    }
                }

                return id is null
                    ? throw new JsonException("A tool reference object must contain an 'id' property.")
                    : new ToolReference(id, toolOptions);
            }

            default:
                throw new JsonException($"Unexpected token '{reader.TokenType}' for a tool reference; expected a string or an object.");
        }
    }

    public override void Write(Utf8JsonWriter writer, ToolReference value, JsonSerializerOptions options)
    {
        if (value.Options is null)
        {
            writer.WriteStringValue(value.Id.ToString());
            return;
        }

        writer.WriteStartObject();
        writer.WriteString("id", value.Id.ToString());
        writer.WritePropertyName("options");
        value.Options.WriteTo(writer, options);
        writer.WriteEndObject();
    }
}
