using System.Collections.Concurrent;
using BTCPayApp.Core.Contracts;

namespace BTCPayApp.Tests;

/// <summary>
/// In-memory <see cref="ConfigProvider"/> for unit tests. Mirrors the abstract
/// surface (Get/Set/List, plus the Updated event) without any DB/IO.
/// </summary>
public class InMemoryConfigProvider : ConfigProvider
{
    private readonly ConcurrentDictionary<string, object?> _data = new();

    public override Task<T?> Get<T>(string key) where T : default
        => Task.FromResult(_data.TryGetValue(key, out var v) ? (T?)v : default);

    public override async Task Set<T>(string key, T? value, bool backup) where T : default
    {
        if (value is null) _data.TryRemove(key, out _);
        else _data[key] = value;
        if (Updated is not null)
            await Updated.Invoke(this, key);
    }

    public override Task<IEnumerable<string>> List(string prefix)
        => Task.FromResult(_data.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)));
}
