using System.Collections.Concurrent;

namespace ExecutionFramework.Session;

using Abstractions;

public class DataContext : IDataContext
{
    private readonly ConcurrentDictionary<string, object> _data = new(StringComparer.OrdinalIgnoreCase);

    public int Count => _data.Count;

    public ICollection<string> Keys => _data.Keys;

    public T GetItem<T>(string key)
    {
        if (_data.TryGetValue(key, out var value))
        {
            if (value is T typedValue)
                return typedValue;
            throw new InvalidCastException(
                $"DataContext 中键 '{key}' 的值的类型为 {value.GetType().Name}，无法转换为 {typeof(T).Name}");
        }
        throw new KeyNotFoundException($"DataContext 中不存在键 '{key}'");
    }

    public void SetItem<T>(string key, T value)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("键不能为空", nameof(key));
        _data[key] = value!;
    }

    public bool ContainsKey(string key)
    {
        return _data.ContainsKey(key);
    }

    public bool RemoveItem(string key)
    {
        return _data.TryRemove(key, out _);
    }

    public void Clear()
    {
        _data.Clear();
    }

    public IEnumerable<string> GetKeys()
    {
        return _data.Keys;
    }

    public object? Get(string key)
    {
        _data.TryGetValue(key, out var value);
        return value;
    }

    public bool TryGet<T>(string key, out T? value)
    {
        if (_data.TryGetValue(key, out var raw) && raw is T typed)
        {
            value = typed;
            return true;
        }
        value = default;
        return false;
    }

    public void SetByPath(string path, object? value)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("路径不能为空", nameof(path));

        if (!path.Contains('.') && !path.Contains('['))
        {
            _data[path] = value!;
            return;
        }

        var segments = ParsePathSegments(path);
        if (segments.Count == 0)
        {
            _data[path] = value!;
            return;
        }

        var rootKey = segments[0].Key;
        if (segments.Count == 1)
        {
            _data[rootKey] = value!;
            return;
        }

        object current = value!;
        for (int i = segments.Count - 1; i > 0; i--)
        {
            var segment = segments[i];
            var parentSegment = segments[i - 1];

            if (segment.IsArray)
            {
                if (segment.Index < 0)
                    throw new ArgumentOutOfRangeException(nameof(path),
                        $"Array index cannot be negative: {segment.Index}");
                if (segment.Index > 10000)
                    throw new ArgumentOutOfRangeException(nameof(path),
                        $"Array index {segment.Index} exceeds maximum allowed size (10000)");

                var list = new List<object?>();
                for (int j = 0; j <= segment.Index; j++)
                    list.Add(null);
                list[segment.Index] = current;
                current = list;
            }
            else
            {
                var dict = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                {
                    [segment.Key] = current!
                };
                current = dict;
            }
        }

        if (segments[0].IsArray)
        {
            var rootList = _data.TryGetValue(rootKey, out var existingList) && existingList is List<object?> exList
                ? exList
                : new List<object?>();
            var index = segments[0].Index;
            while (rootList.Count <= index)
                rootList.Add(null);
            rootList[index] = current;
            _data[rootKey] = rootList;
        }
        else
        {
            if (_data.TryGetValue(rootKey, out var existing) && existing is Dictionary<string, object> existingDict)
            {
                if (current is Dictionary<string, object> newDict)
                {
                    foreach (var kvp in newDict)
                        existingDict[kvp.Key] = kvp.Value;
                }
                else
                {
                    _data[rootKey] = current;
                }
            }
            else
            {
                _data[rootKey] = current;
            }
        }
    }

    public object? GetByPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        if (!path.Contains('.') && !path.Contains('['))
        {
            _data.TryGetValue(path, out var value);
            return value;
        }

        var segments = ParsePathSegments(path);
        if (segments.Count == 0)
            return null;

        var rootKey = segments[0].Key;
        if (!_data.TryGetValue(rootKey, out var current))
            return null;

        for (int i = 1; i < segments.Count; i++)
        {
            var segment = segments[i];

            if (segment.IsArray)
            {
                if (current is System.Collections.IList list && segment.Index < list.Count)
                    current = list[segment.Index];
                else
                    return null;
            }
            else
            {
                if (current is Dictionary<string, object> dict && dict.TryGetValue(segment.Key, out var next))
                    current = next;
                else if (current is IDictionary<string, object> idict && idict.TryGetValue(segment.Key, out var inext))
                    current = inext;
                else
                    return null;
            }
        }

        return current;
    }

    private static List<PathSegment> ParsePathSegments(string path)
    {
        var segments = new List<PathSegment>();
        var parts = path.Split('.');

        foreach (var part in parts)
        {
            if (string.IsNullOrWhiteSpace(part))
                continue;

            var bracketIndex = part.IndexOf('[');
            if (bracketIndex >= 0)
            {
                var key = part[..bracketIndex];
                var indexStr = part[(bracketIndex + 1)..part.IndexOf(']')];
                if (int.TryParse(indexStr, out var index))
                {
                    segments.Add(new PathSegment(key, index));
                }
                else
                {
                    segments.Add(new PathSegment(part, -1, false));
                }
            }
            else
            {
                segments.Add(new PathSegment(part, -1, false));
            }
        }

        return segments;
    }

    private readonly record struct PathSegment(string Key, int Index = -1, bool IsArray = false)
    {
        public PathSegment(string key, int index) : this(key, index, true) { }
        public PathSegment(string key, bool isArray) : this(key, -1, isArray) { }
    }

    public Dictionary<string, object> CreateSnapshot()
    {
        return new Dictionary<string, object>(_data, StringComparer.OrdinalIgnoreCase);
    }

    public void RestoreFromSnapshot(Dictionary<string, object> snapshot)
    {
        if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
        _data.Clear();
        foreach (var kvp in snapshot)
        {
            _data[kvp.Key] = kvp.Value;
        }
    }

    public void Merge(IDataContext other, bool overwrite = true)
    {
        if (other == null) throw new ArgumentNullException(nameof(other));

        foreach (var key in other.GetKeys())
        {
            if (overwrite || !_data.ContainsKey(key))
            {
                _data[key] = other.GetItem<object>(key)!;
            }
        }
    }

    public override string ToString()
    {
        return $"DataContext [Items={Count}]";
    }
}
