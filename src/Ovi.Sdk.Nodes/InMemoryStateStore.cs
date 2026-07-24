using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Ovi.Sdk.Nodes;

/// <summary>
/// The default <see cref="IStateStore"/>: an in-memory, thread-safe store that serializes values to
/// JSON nodes on write. Because serialization happens on <see cref="SetValue{T}"/>, the store is
/// JSON-persistable at any point via <see cref="ToJsonObject"/> and can be rehydrated with
/// <see cref="FromJsonObject"/>.
/// </summary>
public sealed class InMemoryStateStore : IStateStore
{
    private readonly object _gate = new();
    private readonly Dictionary<string, JsonNode?> _values = new(StringComparer.Ordinal);
    private readonly JsonSerializerOptions _serializerOptions;

    public InMemoryStateStore(JsonSerializerOptions? serializerOptions = null) =>
        _serializerOptions = serializerOptions ?? OviJson.DefaultOptions;

    /// <summary>Rehydrates a store from a previously exported <see cref="JsonObject"/>.</summary>
    public static InMemoryStateStore FromJsonObject(JsonObject state, JsonSerializerOptions? serializerOptions = null)
    {
        ArgumentNullException.ThrowIfNull(state);

        var store = new InMemoryStateStore(serializerOptions);
        foreach (var (key, value) in state)
        {
            store._values[key] = value?.DeepClone();
        }

        return store;
    }

    public IReadOnlyCollection<string> Keys
    {
        get
        {
            lock (_gate)
            {
                return [.. _values.Keys];
            }
        }
    }

    public bool ContainsKey(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        lock (_gate)
        {
            return _values.ContainsKey(key);
        }
    }

    public bool TryGetValue<T>(string key, [MaybeNullWhen(false)] out T value)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);

        JsonNode? node;
        lock (_gate)
        {
            if (!_values.TryGetValue(key, out node))
            {
                value = default;
                return false;
            }
        }

        value = node is null ? default! : node.Deserialize<T>(_serializerOptions)!;
        return true;
    }

    public T? GetValueOrDefault<T>(string key, T? defaultValue = default) =>
        TryGetValue<T>(key, out var value) ? value : defaultValue;

    public void SetValue<T>(string key, T value)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);

        // Serialize eagerly so non-serializable values fail here, not at persistence time.
        var node = JsonSerializer.SerializeToNode(value, _serializerOptions);
        lock (_gate)
        {
            _values[key] = node;
        }
    }

    public bool Remove(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        lock (_gate)
        {
            return _values.Remove(key);
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _values.Clear();
        }
    }

    public JsonObject ToJsonObject()
    {
        lock (_gate)
        {
            var result = new JsonObject();
            foreach (var (key, value) in _values)
            {
                result[key] = value?.DeepClone();
            }

            return result;
        }
    }
}
