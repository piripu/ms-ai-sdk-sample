using Ovi.Sdk.Nodes;
using Xunit;

namespace Ovi.Sdk.Tests;

public class StateStoreTests
{
    private sealed record Preferences(string Language, int MaxResults);

    [Fact]
    public void Stores_and_reads_typed_values()
    {
        var store = new InMemoryStateStore();

        store.SetValue("count", 42);
        store.SetValue("name", "ovi");
        store.SetValue("preferences", new Preferences("en", 5));

        Assert.Equal(42, store.GetValueOrDefault<int>("count"));
        Assert.Equal("ovi", store.GetValueOrDefault<string>("name"));
        Assert.Equal(new Preferences("en", 5), store.GetValueOrDefault<Preferences>("preferences"));
        Assert.True(store.ContainsKey("count"));
        Assert.Equal(3, store.Keys.Count);
    }

    [Fact]
    public void Missing_keys_fall_back_to_defaults()
    {
        var store = new InMemoryStateStore();

        Assert.False(store.TryGetValue<int>("missing", out _));
        Assert.Equal(7, store.GetValueOrDefault("missing", 7));
        Assert.Null(store.GetValueOrDefault<string>("missing"));
    }

    [Fact]
    public void State_round_trips_through_json()
    {
        var store = new InMemoryStateStore();
        store.SetValue("count", 42);
        store.SetValue("preferences", new Preferences("en", 5));

        var exported = store.ToJsonObject();
        var rehydrated = InMemoryStateStore.FromJsonObject(exported);

        Assert.Equal(42, rehydrated.GetValueOrDefault<int>("count"));
        Assert.Equal(new Preferences("en", 5), rehydrated.GetValueOrDefault<Preferences>("preferences"));
    }

    [Fact]
    public void Non_serializable_values_fail_fast_on_write()
    {
        var store = new InMemoryStateStore();
        Func<int> nonSerializable = () => 42;

        Assert.Throws<NotSupportedException>(() => store.SetValue("bad", nonSerializable));
    }

    [Fact]
    public void Remove_and_clear_work()
    {
        var store = new InMemoryStateStore();
        store.SetValue("a", 1);
        store.SetValue("b", 2);

        Assert.True(store.Remove("a"));
        Assert.False(store.Remove("a"));
        Assert.False(store.ContainsKey("a"));

        store.Clear();
        Assert.Empty(store.Keys);
    }
}
