using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ovi.Sdk;

/// <summary>
/// The JSON conventions shared across the Ovi SDK (definitions, manifests, and serialized state):
/// camelCase properties, case-insensitive reads, camelCase enum strings, and lenient comment/trailing
/// comma handling for hand-authored files.
/// </summary>
public static class OviJson
{
    /// <summary>Frozen default options for compact output.</summary>
    public static JsonSerializerOptions DefaultOptions { get; } = Freeze(CreateDefaultOptions());

    /// <summary>Frozen default options producing indented, human-friendly output.</summary>
    public static JsonSerializerOptions IndentedOptions { get; } = Freeze(CreateDefaultOptions(writeIndented: true));

    /// <summary>Creates a fresh, modifiable options instance carrying the SDK conventions.</summary>
    public static JsonSerializerOptions CreateDefaultOptions(bool writeIndented = false) => new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = writeIndented,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private static JsonSerializerOptions Freeze(JsonSerializerOptions options)
    {
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
}
