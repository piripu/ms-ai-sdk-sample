using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;

namespace Ovi.Sdk.Operators;

/// <summary>
/// A string-keyed bag of JSON-serializable state. Implementations must guarantee that everything the
/// store holds can round-trip through JSON (see <see cref="ToJsonObject"/>), because workflow, global
/// and instance state are persisted by the runtime between (and across) runs.
/// </summary>
public interface IStateStore
{
    /// <summary>A snapshot of the keys currently in the store.</summary>
    IReadOnlyCollection<string> Keys { get; }

    bool ContainsKey(string key);

    /// <summary>Attempts to read the value stored under <paramref name="key"/> as <typeparamref name="T"/>.</summary>
    bool TryGetValue<T>(string key, [MaybeNullWhen(false)] out T value);

    /// <summary>Reads the value stored under <paramref name="key"/>, or <paramref name="defaultValue"/> when absent.</summary>
    T? GetValueOrDefault<T>(string key, T? defaultValue = default);

    /// <summary>
    /// Stores a value under <paramref name="key"/>. The value is serialized immediately, so a
    /// non-JSON-serializable value fails fast here rather than at persistence time.
    /// </summary>
    void SetValue<T>(string key, T value);

    bool Remove(string key);

    void Clear();

    /// <summary>Exports the entire store as a JSON object suitable for persistence.</summary>
    JsonObject ToJsonObject();
}
