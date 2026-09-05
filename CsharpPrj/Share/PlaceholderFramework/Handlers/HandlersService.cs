
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Graphkey.Core;
using SkeletonFlow;
using Common.Diagnostics;
using Modules.DiagnosticContext;
using Common.PlaceholderFramework.Core;
using Common.PlaceholderFramework.Handlers;
using Modules.Parsing.Abstractions;
using Modules.Parsing.Parsing;

namespace Common.PlaceholderFramework.Integrated.Handlers
{

    public interface IPlaceholderHandler
    {
        string Name { get; }

        bool CanHandle(Graphkey.Core.Placeholder placeholder);

        void Handle(Graphkey.Core.Placeholder placeholder, string resolvedValue,
                    SkeletonContext context, string batchId);
    }


    public class LogPlaceholderHandlerBase : IPlaceholderHandler
    {
        public string Name { get; }

        public string ModuleName { get; }

        public string KeyPrefix { get; }

        public string LogMessagesFileName { get; }

        private static readonly ConcurrentDictionary<string, LogMessageLoader> _loaders = new();

        public static LogPlaceholderHandlerBase Create(string name, string moduleName, string keyPrefix, string logMessagesFileName)
        {
            return new LogPlaceholderHandlerBase(name, moduleName, keyPrefix, logMessagesFileName);
        }

        private LogPlaceholderHandlerBase(string name, string moduleName, string keyPrefix, string logMessagesFileName)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            ModuleName = moduleName ?? throw new ArgumentNullException(nameof(moduleName));
            KeyPrefix = keyPrefix ?? throw new ArgumentNullException(nameof(keyPrefix));
            LogMessagesFileName = logMessagesFileName ?? throw new ArgumentNullException(nameof(logMessagesFileName));
        }

        private LogMessageLoader GetLoader()
        {
            return _loaders.GetOrAdd(KeyPrefix, _ => new LogMessageLoader());
        }

        public static void Reset(string keyPrefix)
        {
            _loaders.TryRemove(keyPrefix, out _);
        }

        public static void Initialize(string jsonFilePath, string keyPrefix)
        {
            var loader = _loaders.GetOrAdd(keyPrefix, _ => new LogMessageLoader());
            loader.Initialize(jsonFilePath, errorMsg =>
            {
                LogRepository.Write(new LogEntry
                {
                    Level = "ERROR",
                    Category = LogCategory.System,
                    Module = "LogPlaceholderHandlerBase",
                    Message = errorMsg
                });
            });
        }

        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, long> _recentlyLogged = new();

        private static bool TryMarkLogged(string key)
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (_recentlyLogged.TryGetValue(key, out var last) && (now - last) < 1)
                return false;
            _recentlyLogged[key] = now;
            return true;
        }

        private static bool TryResolveMessage(string msgId, string moduleName, string keyPrefix, [MaybeNullWhen(false)] out LogMessageDef msgDef)
        {
            if (!_loaders.TryGetValue(keyPrefix, out var loader))
            {
                var callerInfo = GetCallerInfo();
                PlaceholderFramework.Diagnostics.PlaceholderFrameworkDiagnosticContext.Record("placeholder.log.loader_not_initialized", new { keyPrefix, moduleName, msgId });
                LogRepository.Write("WARN", "System", moduleName,
                    $"日志加载器未初始化: keyPrefix='{keyPrefix}'. 请确保 framework.config.json 的 logging.modules 中已配置此 keyPrefix 和对应的 JSON 路径.{callerInfo}");
                msgDef = null;
                return false;
            }
            if (!loader.TryGetMessage(msgId, out msgDef))
            {
                var callerInfo = GetCallerInfo();
                var jsonFile = loader.JsonFilePath ?? "(未知)";
                PlaceholderFramework.Diagnostics.PlaceholderFrameworkDiagnosticContext.Record("placeholder.log.message_not_found", new { msgId, keyPrefix, callerInfo });
                LogRepository.Write("WARN", "System", moduleName,
                    $"日志消息定义未找到: msgId='{msgId}', keyPrefix='{keyPrefix}', 应添加到文件 '{jsonFile}' 的 messages 节点中.{callerInfo}");
                return false;
            }
            return true;
        }

        public static bool Log(string msgId, string moduleName, string keyPrefix, object? parameters = null)
        {
            if (!TryResolveMessage(msgId, moduleName, keyPrefix, out var msgDef))
                return false;

            var paramDict = StringUtils.ConvertParameters(parameters);
            var message = StringUtils.FormatTemplate(msgDef.Template, paramDict);
            LogRepository.Write(msgDef.Level, msgDef.Category, moduleName, message);
            return true;
        }

        public static bool Log(string msgId, string moduleName, string keyPrefix, string message)
        {
            if (!TryResolveMessage(msgId, moduleName, keyPrefix, out var msgDef))
                return false;

            LogRepository.Write(msgDef.Level, msgDef.Category, moduleName, message);
            return true;
        }

        private static string GetCallerInfo()
        {
            try
            {
                var stackTrace = new System.Diagnostics.StackTrace(true);
                for (int i = 0; i < stackTrace.FrameCount; i++)
                {
                    var frame = stackTrace.GetFrame(i);
                    var method = frame?.GetMethod();
                    if (method?.ReflectedType == null) continue;

                    var type = method.ReflectedType;
                    var ns = type.Namespace ?? "";

                    var frameworkTypes = new HashSet<string>
                    {
                        "LogPlaceholderHandlerBase",
                        "LogPlaceholderHandler",
                        "PlaceholderLogHelper",
                        "LogEntryCollector"
                    };
                    if (ns.StartsWith("Common.PlaceholderFramework") ||
                        frameworkTypes.Contains(type.Name))
                        continue;

                    var assemblyName = type.Assembly.GetName().Name;
                    var callerInfo = $" | {type.FullName}.{method.Name}() [{assemblyName}]";

                    var fileName = frame.GetFileName();
                    var lineNumber = frame.GetFileLineNumber();
                    if (!string.IsNullOrEmpty(fileName))
                        callerInfo += $" in {fileName}:line {lineNumber}";

                    return callerInfo;
                }
            }
            catch { }

            return "";
        }

        public bool CanHandle(Graphkey.Core.Placeholder placeholder)
            => placeholder.Key?.StartsWith(KeyPrefix) == true;

        public void Handle(Graphkey.Core.Placeholder placeholder, string resolvedValue,
                           SkeletonContext context, string batchId)
        {
            var msgId = placeholder.Key.Substring(KeyPrefix.Length);

            var loader = GetLoader();
            if (!loader.TryGetMessage(msgId, out var msgDef)) return;

            var dedupKey = $"{KeyPrefix}{msgId}";
            if (!TryMarkLogged(dedupKey))
                return;

            Dictionary<string, string>? parameters = null;
            if (!string.IsNullOrEmpty(resolvedValue))
            {
                try
                {
                    parameters = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(resolvedValue);
                }
                catch { }
            }

            var message = StringUtils.FormatTemplate(msgDef.Template, parameters);

            LogRepository.Write(msgDef.Level, msgDef.Category, ModuleName, message);
        }
    }

    public class LogPlaceholderHandler : IPlaceholderHandler
    {
        public string Name => "Log";

        public bool CanHandle(Graphkey.Core.Placeholder placeholder)
            => placeholder.Key?.StartsWith("log:") == true
               || placeholder.Key?.StartsWith("order:") == true
               || placeholder.Key?.StartsWith("debug:") == true;

        public void Handle(Graphkey.Core.Placeholder placeholder, string resolvedValue,
                           SkeletonContext context, string batchId)
        {
            var moduleName = context.GetModuleName();
            var level = ParseLevel(placeholder.Key);
            var message = FormatMessage(placeholder, resolvedValue);

            var category = ResolveLogCategory(placeholder.Key);
            LogRepository.Write(level, category.ToString(), moduleName, message);
        }

        private static string ParseLevel(string key)
        {
            if (key == null) return "Info";

            if (key.Contains("error", StringComparison.OrdinalIgnoreCase)
                || key.Contains("fail", StringComparison.OrdinalIgnoreCase))
                return "Error";

            if (key.Contains("warn", StringComparison.OrdinalIgnoreCase))
                return "Warn";

            if (key.Contains("debug", StringComparison.OrdinalIgnoreCase))
                return "Debug";

            return "Info";
        }

        private static string FormatMessage(Graphkey.Core.Placeholder placeholder, string resolvedValue)
        {
            var kind = placeholder.Kind.ToString();
            var key = placeholder.Key ?? "unknown";
            return $"[Placeholder:{kind}] {key} = {resolvedValue}";
        }

        private static LogCategory ResolveLogCategory(string key)
        {
            if (string.IsNullOrEmpty(key))
                return LogCategory.System;

            var categoryPart = key;
            if (key.StartsWith("log:")) categoryPart = key.Substring(4);
            else if (key.StartsWith("order:")) categoryPart = key.Substring(6);
            else if (key.StartsWith("debug:")) categoryPart = key.Substring(6);

            var colonIdx = categoryPart.IndexOf(':');
            if (colonIdx > 0)
                categoryPart = categoryPart[..colonIdx];

            return CategoryResolver.Instance.Resolve(categoryPart);
        }
    }



    public class AuditPlaceholderHandler : IPlaceholderHandler
    {
        public string Name => "Audit";

        public bool CanHandle(Graphkey.Core.Placeholder placeholder)
            => placeholder.Key?.StartsWith("audit:") == true
               || placeholder.Key?.StartsWith("trace:") == true;

        public void Handle(Graphkey.Core.Placeholder placeholder, string resolvedValue,
                           SkeletonContext context, string batchId)
        {
            var moduleName = context.GetModuleName();

            var auditEntry = new AuditEntry
            {
                Action = placeholder.Key,
                Data = resolvedValue,
                Source = placeholder.Kind.ToString(),
                SourceModule = moduleName,
                NodeId = placeholder.NodeId,
                NodeIndex = placeholder.NodeIndex,
                Timestamp = DateTime.UtcNow,
                BatchId = batchId
            };

            if (!context.TryGetData<List<AuditEntry>>(out var auditTrail, "__AuditTrail") || auditTrail == null)
            {
                auditTrail = new List<AuditEntry>();
            }
            auditTrail.Add(auditEntry);
            context.SetData(auditTrail, "__AuditTrail");

            PlaceholderFramework.Diagnostics.PlaceholderFrameworkDiagnosticContext.Record("placeholder.audit.entry", new { action = auditEntry.Action, data = auditEntry.Data, batchId });
        }
    }

    public class AuditEntry
    {
        public string Action { get; set; }
        public string Data { get; set; }
        public string Source { get; set; }
        public string SourceModule { get; set; }
        public string NodeId { get; set; }
        public int? NodeIndex { get; set; }
        public DateTime Timestamp { get; set; }
        public string BatchId { get; set; }

        public override string ToString()
            => $"[{Timestamp:yyyy-MM-dd HH:mm:ss}] {Action} | {Data} | Source={Source}";
    }


    public class ConfigPlaceholderHandler : IPlaceholderHandler, IDisposable
    {
        public string Name => "Config";

        private JsonDocument? _configDoc;
        private readonly string _configPath;
        private readonly object _loadLock = new();
        private bool _initialized;

        public ConfigPlaceholderHandler(string? configPath = null)
        {
            _configPath = configPath ?? "test_parameters.json";
        }

        public void Reload()
        {
            lock (_loadLock)
            {
                _configDoc?.Dispose();
                _configDoc = null;
                _initialized = false;
            }
        }

        public bool CanHandle(Graphkey.Core.Placeholder placeholder)
            => placeholder.Key?.StartsWith("config:") == true
               || placeholder.Key?.StartsWith("setting:") == true
               || placeholder.Key?.StartsWith("appsettings:") == true;

        public void Handle(Graphkey.Core.Placeholder placeholder, string resolvedValue,
                           SkeletonContext context, string batchId)
        {
            var configPath = ExtractConfigPath(placeholder.Key);
            if (string.IsNullOrEmpty(configPath))
                return;

            var moduleName = context.GetModuleName();

            var value = !string.IsNullOrEmpty(resolvedValue)
                ? resolvedValue
                : GetValue(configPath);

            if (value == null)
                return;

            var configKey = $"__Config_{configPath.Replace(".", "_")}";
            context.SetData(value, configKey);

            PlaceholderFramework.Diagnostics.PlaceholderFrameworkDiagnosticContext.Record("placeholder.config.resolved", new { moduleName, configPath, value, batchId });
        }

        public string? GetString(string configPath, string? defaultValue = null)
        {
            var element = NavigateTo(configPath);
            return element?.GetString() ?? defaultValue;
        }

        public int GetInt(string configPath, int defaultValue = 0)
        {
            var element = NavigateTo(configPath);
            return element?.GetInt32() ?? defaultValue;
        }

        public double GetDouble(string configPath, double defaultValue = 0.0)
        {
            var element = NavigateTo(configPath);
            return element?.GetDouble() ?? defaultValue;
        }

        public bool GetBool(string configPath, bool defaultValue = false)
        {
            var element = NavigateTo(configPath);
            return element?.GetBoolean() ?? defaultValue;
        }

        public double[]? GetDoubleArray(string configPath)
        {
            var element = NavigateTo(configPath);
            if (element == null) return null;
            var list = new List<double>();
            foreach (var item in element.Value.EnumerateArray())
                list.Add(item.GetDouble());
            return list.ToArray();
        }

        public string[]? GetStringArray(string configPath)
        {
            var element = NavigateTo(configPath);
            if (element == null) return null;
            var list = new List<string>();
            foreach (var item in element.Value.EnumerateArray())
                list.Add(item.GetString() ?? string.Empty);
            return list.ToArray();
        }

        public bool Exists(string configPath)
        {
            return NavigateTo(configPath) != null;
        }

        private static string ExtractConfigPath(string key)
        {
            if (key == null) return string.Empty;

            if (key.StartsWith("config:"))
                return key.Substring(7);
            if (key.StartsWith("setting:"))
                return key.Substring(8);
            if (key.StartsWith("appsettings:"))
                return key.Substring(12);

            return key;
        }

        private string? GetValue(string configPath)
        {
            var element = NavigateTo(configPath);
            if (element == null) return null;

            return element.Value.ValueKind switch
            {
                JsonValueKind.String => element.Value.GetString(),
                JsonValueKind.Number => element.Value.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                JsonValueKind.Array => element.Value.GetRawText(),
                JsonValueKind.Object => element.Value.GetRawText(),
                _ => element.Value.GetRawText()
            };
        }

        private JsonElement? NavigateTo(string configPath)
        {
            EnsureLoaded();
            if (_configDoc == null || string.IsNullOrEmpty(configPath))
                return null;

            return ConfigInjector.NavigateTo(_configDoc.RootElement, configPath);
        }

        private void EnsureLoaded()
        {
            if (_initialized) return;

            lock (_loadLock)
            {
                if (_initialized) return;

                try
                {
                    var paths = new[]
                    {
                        _configPath,
                        Path.Combine(Directory.GetCurrentDirectory(), _configPath),
                        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, _configPath),
                        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", _configPath),
                        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", _configPath)
                    };

                    foreach (var path in paths)
                    {
                        var fullPath = Path.GetFullPath(path);
                        if (File.Exists(fullPath))
                        {
                            var json = File.ReadAllText(fullPath);
                            _configDoc = JsonDocument.Parse(json);
                            _initialized = true;
                            return;
                        }
                    }

                    PlaceholderFramework.Diagnostics.PlaceholderFrameworkDiagnosticContext.Record("placeholder.config.file_not_found", new { configPath = _configPath });
                    _configDoc = JsonDocument.Parse("{}");
                    _initialized = true;
                }
                catch (Exception ex)
                {
                    PlaceholderFramework.Diagnostics.PlaceholderFrameworkDiagnosticContext.Record("placeholder.config.load_failed", new { configPath = _configPath, error = ex.Message });
                    _configDoc = JsonDocument.Parse("{}");
                    _initialized = true;
                }
            }
        }

        public void Dispose()
        {
            _configDoc?.Dispose();
        }
    }


    public class MonitorPlaceholderHandler : IPlaceholderHandler
    {
        public string Name => "Monitor";

        public bool CanHandle(Graphkey.Core.Placeholder placeholder)
            => placeholder.Key?.StartsWith("monitor:") == true
               || placeholder.Key?.StartsWith("metric:") == true
               || placeholder.Key?.StartsWith("alert:") == true;

        public void Handle(Graphkey.Core.Placeholder placeholder, string resolvedValue,
                           SkeletonContext context, string batchId)
        {
            var moduleName = context.GetModuleName();

            if (!context.TryGetData<Dictionary<string, object>>(out var metrics, "__MonitorMetrics") || metrics == null)
            {
                metrics = new Dictionary<string, object>();
            }
            metrics[placeholder.Key] = resolvedValue;
            context.SetData(metrics, "__MonitorMetrics");

            if (double.TryParse(resolvedValue, out var numericValue))
            {
                EvaluateAlertRules(placeholder.Key, numericValue, context, batchId);
            }

            PlaceholderFramework.Diagnostics.PlaceholderFrameworkDiagnosticContext.Record("placeholder.monitor.metric", new { moduleName, key = placeholder.Key, value = resolvedValue, batchId });
        }

        private static void EvaluateAlertRules(string key, double value,
                                                SkeletonContext context, string batchId)
        {
            if (!context.TryGetData<Dictionary<string, double>>(out var thresholds, "__AlertThresholds") || thresholds == null)
                return;

            if (thresholds.TryGetValue(key, out var threshold) && value > threshold)
            {
                if (!context.TryGetData<List<string>>(out var alerts, "__Alerts") || alerts == null)
                {
                    alerts = new List<string>();
                }
                alerts.Add($"[ALERT] {key} = {value} (threshold: {threshold}) | Batch={batchId}");
                context.SetData(alerts, "__Alerts");

                PlaceholderFramework.Diagnostics.PlaceholderFrameworkDiagnosticContext.Record("placeholder.monitor.alert", new { key, value, threshold });
            }
        }
    }


    public class PerformancePlaceholderHandler : IPlaceholderHandler
    {
        public string Name => "Performance";

        public bool CanHandle(Graphkey.Core.Placeholder placeholder)
            => placeholder.Key?.StartsWith("perf:") == true
               || placeholder.Key?.StartsWith("timing:") == true
               || placeholder.Key?.StartsWith("duration:") == true;

        public void Handle(Graphkey.Core.Placeholder placeholder, string resolvedValue,
                           SkeletonContext context, string batchId)
        {
            var moduleName = context.GetModuleName();

            if (double.TryParse(resolvedValue, out var duration))
            {
                if (!context.TryGetData<List<PerformanceRecord>>(out var perfData, "__PerformanceData") || perfData == null)
                {
                    perfData = new List<PerformanceRecord>();
                }
                perfData.Add(new PerformanceRecord
                {
                    Key = placeholder.Key,
                    Duration = duration,
                    Module = moduleName,
                    Kind = placeholder.Kind.ToString(),
                    Timestamp = DateTime.UtcNow,
                    BatchId = batchId
                });
                context.SetData(perfData, "__PerformanceData");

                if (!context.TryGetData<PerformanceStats>(out var stats, "__PerformanceStats") || stats == null)
                {
                    stats = new PerformanceStats();
                }
                stats.Record(placeholder.Key, duration);
                context.SetData(stats, "__PerformanceStats");
            }

            PlaceholderFramework.Diagnostics.PlaceholderFrameworkDiagnosticContext.Record("placeholder.perf.metric", new { key = placeholder.Key, durationMs = resolvedValue, moduleName });
        }
    }

    public class PerformanceRecord
    {
        public string Key { get; set; }
        public double Duration { get; set; }
        public string Module { get; set; }
        public string Kind { get; set; }
        public DateTime Timestamp { get; set; }
        public string BatchId { get; set; }
    }

    public class PerformanceStats
    {
        private readonly ConcurrentDictionary<string, List<double>> _records = new();

        public void Record(string key, double duration)
        {
            _records.AddOrUpdate(
                key,
                _ => new List<double> { duration },
                (_, list) =>
                {
                    lock (list)
                    {
                        list.Add(duration);
                    }
                    return list;
                });
        }

        public double GetAverage(string key)
            => _records.TryGetValue(key, out var list) && list.Count > 0
                ? list.Average() : 0;

        public double GetMax(string key)
            => _records.TryGetValue(key, out var list) && list.Count > 0
                ? list.Max() : 0;

        public double GetMin(string key)
            => _records.TryGetValue(key, out var list) && list.Count > 0
                ? list.Min() : 0;

        public int GetCount(string key)
            => _records.TryGetValue(key, out var list) ? list.Count : 0;

        public string GetSummary()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var kvp in _records)
            {
                var list = kvp.Value;
                lock (list)
                {
                    sb.AppendLine($"{kvp.Key}: count={list.Count}, avg={list.Average():F2}ms, " +
                                  $"min={list.Min():F2}ms, max={list.Max():F2}ms");
                }
            }
            return sb.ToString();
        }
    }


    public class HandlersService
    {
        private readonly List<IPlaceholderHandler> _handlers = new();
        private readonly object _lock = new();

        public IReadOnlyList<IPlaceholderHandler> Handlers
        {
            get { lock (_lock) return _handlers.ToList(); }
        }

        public void Register(IPlaceholderHandler handler)
        {
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));

            lock (_lock)
            {
                var existing = _handlers.FirstOrDefault(h =>
                    string.Equals(h.Name, handler.Name, StringComparison.OrdinalIgnoreCase));
                if (existing != null)
                    _handlers.Remove(existing);

                _handlers.Add(handler);
            }
        }

        public void RegisterRange(IEnumerable<IPlaceholderHandler> handlers)
        {
            foreach (var handler in handlers)
                Register(handler);
        }

        public IPlaceholderHandler? FindHandler(Graphkey.Core.Placeholder placeholder)
        {
            lock (_lock)
            {
                return _handlers.FirstOrDefault(h => h.CanHandle(placeholder));
            }
        }

        public IPlaceholderHandler? FindByName(string name)
        {
            lock (_lock)
            {
                return _handlers.FirstOrDefault(h =>
                    string.Equals(h.Name, name, StringComparison.OrdinalIgnoreCase));
            }
        }

        public bool Remove(string name)
        {
            lock (_lock)
            {
                var handler = _handlers.FirstOrDefault(h =>
                    string.Equals(h.Name, name, StringComparison.OrdinalIgnoreCase));
                if (handler != null)
                    return _handlers.Remove(handler);
                return false;
            }
        }

        public void Clear()
        {
            lock (_lock)
            {
                _handlers.Clear();
            }
        }

        public int Count
        {
            get { lock (_lock) return _handlers.Count; }
        }

        public void RegisterDefaults()
        {
            RegisterRange(new IPlaceholderHandler[]
            {
                new LogPlaceholderHandler(),
                new AuditPlaceholderHandler(),
                new MonitorPlaceholderHandler(),
                new PerformancePlaceholderHandler()
            });
        }



        public HandlerCommandRouter RegisterToCommandRouter(ICommandRouter router)
        {
            if (router == null)
                throw new ArgumentNullException(nameof(router));

            var handlerRouter = new HandlerCommandRouter(router);

            lock (_lock)
            {
                foreach (var handler in _handlers)
                {
                    var structureHandler = new PlaceholderHandlerAdapter(handler);
                    handlerRouter.RegisterHandler(structureHandler);
                }
            }

            return handlerRouter;
        }

        private class PlaceholderHandlerAdapter : Core.IStructureHandler
        {
            private readonly IPlaceholderHandler _handler;

            public string Name => _handler.Name;

            public PlaceholderHandlerAdapter(IPlaceholderHandler handler)
            {
                _handler = handler ?? throw new ArgumentNullException(nameof(handler));
            }

            public bool CanHandle(object doc)
            {
                if (doc is Graphkey.Core.Placeholder placeholder)
                    return _handler.CanHandle(placeholder);

                if (doc is string key)
                {
                    var testPlaceholder = Graphkey.Core.Placeholder.From(key);
                    return _handler.CanHandle(testPlaceholder);
                }

                return false;
            }

            public void Process(object doc, TConfig.ErrorCollector errors,
                Modules.Pipeline.PipelineContext context)
            {
                if (doc is Graphkey.Core.Placeholder placeholder)
                {
                    var resolvedValue = context?.GetItem<string>("resolvedValue") ?? string.Empty;
                    var batchId = context?.GetItem<string>("batchId") ?? Guid.NewGuid().ToString("N");

                    var skeletonCtx = new SkeletonFlow.SkeletonContext();
                    if (context?.SourceFile != null)
                        skeletonCtx.SetData(context.SourceFile, "__SourceFile");

                    _handler.Handle(placeholder, resolvedValue, skeletonCtx, batchId);
                }
                else if (doc is string key)
                {
                    var testPlaceholder = Graphkey.Core.Placeholder.From(key);
                    var resolvedValue = context?.GetItem<string>("resolvedValue") ?? key;
                    var batchId = context?.GetItem<string>("batchId") ?? Guid.NewGuid().ToString("N");
                    var skeletonCtx = new SkeletonFlow.SkeletonContext();

                    _handler.Handle(testPlaceholder, resolvedValue, skeletonCtx, batchId);
                }
                else
                {
                    errors.Add("INVALID_INPUT",
                        $"Cannot process input of type '{doc?.GetType().Name}'. Expected Placeholder or string.",
                        TConfig.ErrorLevel.Error);
                }
            }
        }
    }
}
