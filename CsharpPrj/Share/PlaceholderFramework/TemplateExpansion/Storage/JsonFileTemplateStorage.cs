
using System.Text.Json;
using System.Text.Json.Nodes;
using Plate.JsonMethod.Serialization;
using Common.PlaceholderFramework.TemplateExpansion.Abstractions;

namespace Common.PlaceholderFramework.TemplateExpansion.Storage;

public class JsonFileTemplateStorage : ITemplateStorage, IDisposable
{
    private readonly object _lock = new();
    private JsonObject _styles = new();
    private JsonObject _composites = new();
    private string? _stylesPath;
    private string? _compositesPath;
    private readonly List<FileSystemWatcher> _watchers = new();
    private bool _initialized;
    private bool _disposed;

    private readonly List<(string stylesPath, string compositesPath)> _additionalPaths = new();

    public bool IsInitialized => _initialized;

    public string? StylesPath => _stylesPath;

    public string? CompositesPath => _compositesPath;

    public void Initialize(string stylesPath, string compositesPath)
    {
        if (string.IsNullOrEmpty(stylesPath))
            throw new ArgumentNullException(nameof(stylesPath));
        if (string.IsNullOrEmpty(compositesPath))
            throw new ArgumentNullException(nameof(compositesPath));

        lock (_lock)
        {
            _stylesPath = Path.GetFullPath(stylesPath);
            _compositesPath = Path.GetFullPath(compositesPath);
            LoadInternal();
            StartFileWatchers();
            _initialized = true;
        }
    }

    public void Initialize(IEnumerable<(string stylesPath, string compositesPath)> paths)
    {
        var pathList = paths?.ToList();
        if (pathList == null || pathList.Count == 0)
            throw new ArgumentException("At least one set of template paths is required.", nameof(paths));

        lock (_lock)
        {
            var (firstStyles, firstComposites) = pathList[0];
            _stylesPath = Path.GetFullPath(firstStyles);
            _compositesPath = Path.GetFullPath(firstComposites);

            _additionalPaths.Clear();
            for (int i = 1; i < pathList.Count; i++)
            {
                var (sPath, cPath) = pathList[i];
                _additionalPaths.Add((Path.GetFullPath(sPath), Path.GetFullPath(cPath)));
            }

            LoadInternal();
            StartFileWatchers();
            _initialized = true;
        }
    }

    public JsonObject? GetStyle(string name)
    {
        EnsureInitialized();
        lock (_lock)
        {
            if (!_styles.TryGetPropertyValue(name, out var node))
                return null;
            return node?.AsObject();
        }
    }

    public JsonObject? GetComposite(string name)
    {
        EnsureInitialized();
        lock (_lock)
        {
            if (!_composites.TryGetPropertyValue(name, out var node))
                return null;
            return node?.AsObject();
        }
    }

    public bool HasStyle(string name)
    {
        return GetStyle(name) != null;
    }

    public bool HasComposite(string name)
    {
        return GetComposite(name) != null;
    }

    public IEnumerable<string> GetStyleNames()
    {
        EnsureInitialized();
        lock (_lock)
        {
            return _styles.Select(kv => kv.Key).ToList();
        }
    }

    public IEnumerable<string> GetCompositeNames()
    {
        EnsureInitialized();
        lock (_lock)
        {
            return _composites.Select(kv => kv.Key).ToList();
        }
    }

    public TemplateMetadata? GetMetadata(string name)
    {
        EnsureInitialized();
        lock (_lock)
        {
            if (_composites.TryGetPropertyValue(name, out var compositeNode) && compositeNode is JsonObject compositeObj)
            {
                var meta = new TemplateMetadata
                {
                    Name = name,
                    Type = "composite",
                    SourceFile = _compositesPath,
                    RegisteredAt = DateTime.UtcNow,
                    HasParameters = compositeObj.ContainsKey("parameters"),
                    HasDefaults = compositeObj.ContainsKey("defaults"),
                    HasApplyTo = compositeObj.ContainsKey("applyTo"),
                    IsMultiObject = IsMultiObjectTemplate(compositeObj)
                };

                if (compositeObj.TryGetPropertyValue("description", out var descNode) && descNode is JsonValue descVal)
                    meta.Description = descVal.GetValue<string>();

                return meta;
            }

            if (_styles.TryGetPropertyValue(name, out var styleNode) && styleNode is JsonObject styleObj)
            {
                var meta = new TemplateMetadata
                {
                    Name = name,
                    Type = "style",
                    SourceFile = _stylesPath,
                    RegisteredAt = DateTime.UtcNow,
                    HasApplyTo = styleObj.ContainsKey("applyTo")
                };

                if (styleObj.TryGetPropertyValue("description", out var descNode) && descNode is JsonValue descVal)
                    meta.Description = descVal.GetValue<string>();

                return meta;
            }

            return null;
        }
    }

    public IEnumerable<string> SearchTemplates(string keyword)
    {
        EnsureInitialized();
        lock (_lock)
        {
            var results = new List<string>();

            if (string.IsNullOrEmpty(keyword))
            {
                results.AddRange(GetCompositeNames());
                results.AddRange(GetStyleNames());
            }
            else
            {
                var lowerKeyword = keyword.ToLowerInvariant();

                if (_composites.Count > 0)
                {
                    results.AddRange(_composites
                        .Where(kv => kv.Key.Contains(lowerKeyword, StringComparison.OrdinalIgnoreCase))
                        .Select(kv => kv.Key));
                }

                if (_styles.Count > 0)
                {
                    results.AddRange(_styles
                        .Where(kv => kv.Key.Contains(lowerKeyword, StringComparison.OrdinalIgnoreCase))
                        .Select(kv => kv.Key));
                }
            }

            return results;
        }
    }

    private static bool IsMultiObjectTemplate(JsonObject obj)
    {
        if (obj.TryGetPropertyValue("type", out var typeNode) &&
            typeNode?.GetValue<string>() == "command-group")
            return true;

        if (obj.ContainsKey("controls") && obj["controls"] is JsonArray)
            return true;

        return false;
    }

    public void RegisterComposite(string name, JsonObject definition)
    {
        if (string.IsNullOrEmpty(name))
            throw new ArgumentNullException(nameof(name));
        if (definition == null)
            throw new ArgumentNullException(nameof(definition));

        lock (_lock)
        {
            _composites[name] = definition.DeepClone();
        }
    }

    public void RegisterStyle(string name, JsonObject definition)
    {
        if (string.IsNullOrEmpty(name))
            throw new ArgumentNullException(nameof(name));
        if (definition == null)
            throw new ArgumentNullException(nameof(definition));

        lock (_lock)
        {
            _styles[name] = definition.DeepClone();
        }
    }

    public void UnregisterComposite(string name)
    {
        if (string.IsNullOrEmpty(name)) return;

        lock (_lock)
        {
            _composites?.Remove(name);
        }
    }

    public void UnregisterStyle(string name)
    {
        if (string.IsNullOrEmpty(name)) return;

        lock (_lock)
        {
            _styles?.Remove(name);
        }
    }

    #region 批量操作

    public void RegisterComposites(Dictionary<string, JsonObject> definitions)
    {
        if (definitions == null)
            throw new ArgumentNullException(nameof(definitions));

        lock (_lock)
        {
            foreach (var kv in definitions)
            {
                _composites[kv.Key] = kv.Value.DeepClone();
            }
        }
    }

    public void RegisterStyles(Dictionary<string, JsonObject> definitions)
    {
        if (definitions == null)
            throw new ArgumentNullException(nameof(definitions));

        lock (_lock)
        {
            foreach (var kv in definitions)
            {
                _styles[kv.Key] = kv.Value.DeepClone();
            }
        }
    }

    public void UnregisterComposites(IEnumerable<string> names)
    {
        if (names == null) return;

        lock (_lock)
        {
            foreach (var name in names)
            {
                _composites.Remove(name);
            }
        }
    }

    public void UnregisterStyles(IEnumerable<string> names)
    {
        if (names == null) return;

        lock (_lock)
        {
            foreach (var name in names)
            {
                _styles.Remove(name);
            }
        }
    }

    public void ClearComposites()
    {
        lock (_lock)
        {
            _composites.Clear();
        }
    }

    public void ClearStyles()
    {
        lock (_lock)
        {
            _styles.Clear();
        }
    }

    #endregion

    public void RefreshIfNeeded()
    {
    }

    private void LoadInternal()
    {
        _styles = LoadStylesFile(_stylesPath);
        _composites = LoadCompositesFile(_compositesPath);

        foreach (var (sPath, cPath) in _additionalPaths)
        {
            var additionalStyles = LoadStylesFile(sPath);
            foreach (var kv in additionalStyles)
                _styles[kv.Key] = kv.Value?.DeepClone();

            var additionalComposites = LoadCompositesFile(cPath);
            foreach (var kv in additionalComposites)
                _composites[kv.Key] = kv.Value?.DeepClone();
        }
    }

    private static JsonObject LoadStylesFile(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            return new JsonObject();

        var doc = JsonSerializationService.Instance.ParseJsonNode(File.ReadAllText(path));
        return doc?["styles"]?.AsObject() ?? new JsonObject();
    }

    private static JsonObject LoadCompositesFile(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            return new JsonObject();

        var doc = JsonSerializationService.Instance.ParseJsonNode(File.ReadAllText(path));
        return doc?["composites"]?.AsObject() ?? new JsonObject();
    }

    private void StartFileWatchers()
    {
        DisposeWatchers();

        TryWatchFile(_stylesPath);
        TryWatchFile(_compositesPath);

        foreach (var (sPath, cPath) in _additionalPaths)
        {
            TryWatchFile(sPath);
            TryWatchFile(cPath);
        }
    }

    private void TryWatchFile(string? filePath)
    {
        if (string.IsNullOrEmpty(filePath))
            return;

        var dir = Path.GetDirectoryName(filePath) ?? ".";
        var fileName = Path.GetFileName(filePath);
        var watcher = new FileSystemWatcher(dir, fileName)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.CreationTime,
            EnableRaisingEvents = true
        };
        watcher.Changed += OnFileChanged;
        watcher.Created += OnFileChanged;
        _watchers.Add(watcher);
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        Thread.Sleep(100);
        lock (_lock)
        {
            if (_disposed) return;
            try
            {
                LoadInternal();
            }
            catch
            {
            }
        }
    }

    private void DisposeWatchers()
    {
        foreach (var watcher in _watchers)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Changed -= OnFileChanged;
            watcher.Created -= OnFileChanged;
            watcher.Dispose();
        }
        _watchers.Clear();
    }

    private void EnsureInitialized()
    {
        if (!_initialized)
            throw new InvalidOperationException(
                "TemplateStorage has not been initialized. Call Initialize() first.");
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            DisposeWatchers();
        }
    }
}
