using System.Text.Json;

namespace Battery.Core;

/// <summary>
/// The last reading per provider, kept on disk so a restart shows the previous bars immediately.
/// Thread-safe; never throws for a missing, corrupt or unwritable file.
/// </summary>
public sealed class ReadingStore
{
    readonly string _path;
    readonly object _gate = new();
    Dictionary<string, ProviderReading> _current;

    public ReadingStore(string path)
    {
        _path = path;
        _current = Load(path);
    }

    /// <summary>An immutable snapshot; later puts produce a new one.</summary>
    public IReadOnlyDictionary<string, ProviderReading> Current
    {
        get { lock (_gate) return _current; }
    }

    /// <summary>Raised after a reading was accepted, on the thread that called Put.</summary>
    public event Action? Changed;

    /// <summary>Stores a reading unless a newer one for the same provider is already held.</summary>
    public void Put(ProviderReading r)
    {
        lock (_gate)
        {
            if (_current.TryGetValue(r.ProviderId, out var existing) && r.ReadAt < existing.ReadAt) return;
            _current = new Dictionary<string, ProviderReading>(_current) { [r.ProviderId] = r };
            Save(_current);
        }
        Changed?.Invoke();
    }

    static Dictionary<string, ProviderReading> Load(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                var loaded = JsonSerializer.Deserialize<Dictionary<string, ProviderReading>>(File.ReadAllText(path));
                if (loaded is not null)
                {
                    return loaded
                        .Where(kv => kv.Value is { Windows: not null, ProviderId: not null }
                                     && kv.Value.Windows.All(w => w is not null))
                        .ToDictionary(kv => kv.Key, kv => kv.Value);
                }
            }
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // Corrupt or unreadable: start empty; the next Put rewrites the file.
        }
        return new Dictionary<string, ProviderReading>();
    }

    void Save(Dictionary<string, ProviderReading> readings)
    {
        try
        {
            var dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(readings));
            File.Move(tmp, _path, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Keep the reading in memory; persistence is best-effort.
        }
    }
}
