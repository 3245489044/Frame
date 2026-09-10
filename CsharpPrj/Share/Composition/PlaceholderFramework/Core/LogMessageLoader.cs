using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace Common.PlaceholderFramework.Core
{
    public class LogMessageDef
    {
        public string? Level { get; set; }
        public string? Category { get; set; }
        public string? Template { get; set; }
    }

    public class LogMessageLoader
    {
        private Dictionary<string, LogMessageDef>? _messages;
        private readonly object _loadLock = new();
        private bool _initialized;
        private string? _jsonFilePath;
        private Action<string>? _onError;

        public string? JsonFilePath => _jsonFilePath;

        public void Initialize(string jsonFilePath, Action<string>? onError = null)
        {
            if (string.IsNullOrEmpty(jsonFilePath))
                throw new ArgumentNullException(nameof(jsonFilePath));
            if (!File.Exists(jsonFilePath))
                throw new FileNotFoundException($"Log messages JSON not found: {jsonFilePath}");

            _jsonFilePath = jsonFilePath;
            _onError = onError;

            lock (_loadLock)
            {
                LoadInternal();
            }
        }

        public void Reload()
        {
            if (_jsonFilePath == null) return;

            lock (_loadLock)
            {
                LoadInternal();
            }
        }

        private void LoadInternal()
        {
            try
            {
                var json = File.ReadAllText(_jsonFilePath!);
                var doc = JsonDocument.Parse(json);
                var messages = new Dictionary<string, LogMessageDef>();

                foreach (var prop in doc.RootElement.GetProperty("messages").EnumerateObject())
                {
                    var msg = prop.Value;
                    messages[prop.Name] = new LogMessageDef
                    {
                        Level = msg.GetProperty("level").GetString(),
                        Category = msg.GetProperty("category").GetString(),
                        Template = msg.GetProperty("template").GetString()
                    };
                }

                Volatile.Write(ref _messages, messages);
                _initialized = true;
            }
            catch (Exception ex)
            {
                _onError?.Invoke($"[LogMessageLoader] Failed to initialize from '{_jsonFilePath}': {ex.Message}");
                if (_messages == null)
                {
                    Volatile.Write(ref _messages, new Dictionary<string, LogMessageDef>());
                }
                _initialized = true;
            }
        }

        public bool TryGetMessage(string msgId, [MaybeNullWhen(false)] out LogMessageDef msgDef)
        {
            EnsureLoaded();
            var messages = Volatile.Read(ref _messages);
            if (messages != null && messages.TryGetValue(msgId, out var def))
            {
                msgDef = def;
                return true;
            }
            msgDef = null;
            return false;
        }

        public IReadOnlyDictionary<string, LogMessageDef>? GetAllMessages()
        {
            EnsureLoaded();
            return Volatile.Read(ref _messages);
        }

        public bool IsInitialized => _initialized;

        public int Count
        {
            get
            {
                EnsureLoaded();
                return Volatile.Read(ref _messages)?.Count ?? 0;
            }
        }

        private void EnsureLoaded()
        {
            var messages = Volatile.Read(ref _messages);
            if (messages != null) return;

            if (!_initialized)
            {
                lock (_loadLock)
                {
                    if (Volatile.Read(ref _messages) == null)
                    {
                        Volatile.Write(ref _messages, new Dictionary<string, LogMessageDef>());
                    }
                }
                return;
            }

            lock (_loadLock)
            {
                if (Volatile.Read(ref _messages) == null)
                {
                    Volatile.Write(ref _messages, new Dictionary<string, LogMessageDef>());
                }
            }
        }
    }
}
