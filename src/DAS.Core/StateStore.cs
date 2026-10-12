using System.Text.Json;

namespace DAS.Core;

public sealed record StateEntry(string UserHash, AccountStatus Status, int Attempts, string? Error, DateTimeOffset Ts);

/// <summary>state.json：续跑与去重的唯一真相。</summary>
public sealed class StateStore
{
    private readonly string _path;
    private readonly Dictionary<string, StateEntry> _map = new();

    public StateStore(string path)
    {
        _path = path;
        if (!File.Exists(path)) return;
        var loaded = JsonSerializer.Deserialize<Dictionary<string, StateEntry>>(File.ReadAllText(path));
        if (loaded is not null) _map = loaded;
    }

    public bool IsCompleted(string hash) =>
        _map.TryGetValue(hash, out var e) && e.Status is AccountStatus.Ok or AccountStatus.BadCred;

    public void Set(string hash, AccountStatus status, int attempts, string? error = null)
    {
        _map[hash] = new StateEntry(hash, status, attempts, error, DateTimeOffset.Now);
        Save();
    }

    private void Save() =>
        File.WriteAllText(_path, JsonSerializer.Serialize(_map, new JsonSerializerOptions { WriteIndented = true }));
}
