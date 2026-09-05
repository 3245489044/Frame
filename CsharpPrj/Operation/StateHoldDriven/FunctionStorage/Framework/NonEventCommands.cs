
using System.Collections.Concurrent;
using IExecutionContext = TConfig.IExecutionContext;
using IParsedCommand = CommandFramework.Abstractions.IParsedCommand;
using CommandCriticality = CommandFramework.Abstractions.CommandCriticality;
using INonEventCommandRegistry = CommandFramework.Abstractions.INonEventCommandRegistry;
using NonEventCommandValidationResult = CommandFramework.Abstractions.NonEventCommandValidationResult;
using FunctionStorage.Abstractions;
using CommandFramework.Parsing;

namespace FunctionStorage.Core
{




    public class NonEventCommandRegistry : INonEventCommandRegistry
    {
        private readonly ConcurrentDictionary<string, NonEventCommandEntry> _handlers = new(StringComparer.OrdinalIgnoreCase);

        private readonly ConcurrentDictionary<string, CommandCriticality> _declaredCommandNames = new(StringComparer.OrdinalIgnoreCase);

        private bool _validationPerformed;
        private NonEventCommandValidationResult? _lastValidationResult;

        public IReadOnlyCollection<string> RegisteredCommandNames =>
            _handlers.Keys.ToList().AsReadOnly();

        public int Count => _handlers.Count;

        public void Register(
            string commandName,
            Action<IExecutionContext> handler,
            CommandCriticality criticality = CommandCriticality.Important,
            string? description = null)
        {
            if (string.IsNullOrWhiteSpace(commandName))
                throw new ArgumentException("Command name cannot be null or empty", nameof(commandName));
            ArgumentNullException.ThrowIfNull(handler);

            _handlers[commandName] = new NonEventCommandEntry
            {
                CommandName = commandName,
                Handler = handler,
                Criticality = criticality,
                Description = description ?? $"Non-event command: {commandName}",
                RegisteredAt = DateTime.UtcNow
            };

            _declaredCommandNames[commandName] = criticality;

            _validationPerformed = false;

        }

        public void Declare(string commandName, CommandCriticality criticality = CommandCriticality.Important)
        {
            if (string.IsNullOrWhiteSpace(commandName))
                throw new ArgumentException("Command name cannot be null or empty", nameof(commandName));

            _declaredCommandNames[commandName] = criticality;


            _validationPerformed = false;
            _lastValidationResult = null;
        }


        public void RegisterRange(params (string Name, Action<IExecutionContext> Handler,
            CommandCriticality Criticality, string? Description)[] commands)
        {
            foreach (var (name, handler, criticality, description) in commands)
            {
                Register(name, handler, criticality, description);
            }
        }

        public bool TryGetHandler(string commandName, out Action<IExecutionContext>? handler)
        {
            if (_handlers.TryGetValue(commandName, out var entry))
            {
                handler = entry.Handler;
                return true;
            }
            handler = null;
            return false;
        }

        public bool IsRegistered(string commandName)
        {
            return _handlers.ContainsKey(commandName);
        }

        public bool Contains(string commandName)
        {
            return _declaredCommandNames.ContainsKey(commandName);
        }

        public NonEventCommandValidationResult ValidateAll()
        {
            if (_validationPerformed && _lastValidationResult != null)
                return _lastValidationResult;

            var allDeclared = _declaredCommandNames.Keys.ToList();
            var missingHandlers = new List<string>();
            var criticalMissing = new List<string>();

            foreach (var cmd in allDeclared)
            {
                if (!_handlers.TryGetValue(cmd, out var entry) || entry.Handler == null)
                {
                    missingHandlers.Add(cmd);
                    if (_declaredCommandNames.TryGetValue(cmd, out var criticality) &&
                        criticality == CommandCriticality.Critical)
                    {
                        criticalMissing.Add(cmd);
                    }
                }
            }

            _validationPerformed = true;

            var result = new NonEventCommandValidationResult
            {
                TotalCommands = allDeclared.Count,
                RegisteredCount = allDeclared.Count - missingHandlers.Count,
                MissingHandlers = missingHandlers.ToArray(),
                CriticalMissing = criticalMissing.ToArray(),
                IsValid = missingHandlers.Count == 0,
                ValidationTime = DateTime.UtcNow
            };

            _lastValidationResult = result;
            return result;
        }


        public string[] GetUnhandledCommands()
        {
            return _declaredCommandNames.Keys
                .Where(cmd => !_handlers.TryGetValue(cmd, out var entry) || entry.Handler == null)
                .ToArray();
        }

        public bool Unregister(string commandName)
        {
            var removed = _handlers.TryRemove(commandName, out _);
            if (removed)
            {
                _declaredCommandNames.TryRemove(commandName, out _);
                _validationPerformed = false;
                _lastValidationResult = null;
            }
            return removed;
        }


        public NonEventCommandEntry? GetEntry(string commandName)
        {
            if (_handlers.TryGetValue(commandName, out var entry))
            {
                return entry;
            }
            return null;
        }

        public void Clear()
        {
            _handlers.Clear();
            _declaredCommandNames.Clear();
            _validationPerformed = false;
            _lastValidationResult = null;
        }

        public void ResetValidation()
        {
            _validationPerformed = false;
            _lastValidationResult = null;
        }

    }


    public class NonEventCommandEntry
    {
        public string CommandName { get; set; } = string.Empty;

        public Action<IExecutionContext> Handler { get; set; } = null!;

        public CommandCriticality Criticality { get; set; } = CommandCriticality.Important;

        public string Description { get; set; } = string.Empty;

        public DateTime RegisteredAt { get; set; }
    }


    public static class BindingRegistration
    {
        public enum BindingSource
        {
            DirectLoad,
            Sideload,
            GuiConfig,
            ExternalApi
        }

        public class BindingRecord
        {
            public string ConfigName { get; init; } = string.Empty;
            public BindingSource Source { get; init; }
            public DateTime RegisteredAt { get; init; } = DateTime.UtcNow;
            public bool IsSuccess { get; init; }
            public string? ErrorMessage { get; init; }
        }

        private static readonly ConcurrentBag<BindingRecord> _registrationHistory = new();

        public static IReadOnlyCollection<BindingRecord> RegistrationHistory =>
            _registrationHistory.ToList().AsReadOnly();

        public static void RecordRegistration(
            string configName,
            BindingSource source,
            bool isSuccess,
            string? errorMessage = null)
        {
            _registrationHistory.Add(new BindingRecord
            {
                ConfigName = configName,
                Source = source,
                RegisteredAt = DateTime.UtcNow,
                IsSuccess = isSuccess,
                ErrorMessage = errorMessage
            });
        }

        public static IReadOnlyCollection<BindingRecord> GetRecordsBySource(BindingSource source)
        {
            return _registrationHistory
                .Where(r => r.Source == source)
                .ToList()
                .AsReadOnly();
        }

        public static IReadOnlyCollection<BindingRecord> GetRecentRecords(int count = 10)
        {
            return _registrationHistory
                .OrderByDescending(r => r.RegisteredAt)
                .Take(count)
                .ToList()
                .AsReadOnly();
        }

        public static string GetStatistics()
        {
            var total = _registrationHistory.Count;
            var successCount = _registrationHistory.Count(r => r.IsSuccess);
            var failCount = total - successCount;
            var bySource = _registrationHistory
                .GroupBy(r => r.Source)
                .Select(g => $"  {g.Key}: {g.Count()} 次")
                .ToList();

            return $"绑定注册统计：\n" +
                   $"  总计: {total} 次\n" +
                   $"  成功: {successCount} 次\n" +
                   $"  失败: {failCount} 次\n" +
                   $"  按来源:\n" +
                   $"{string.Join("\n", bySource)}";
        }
    }


    public class SideloadRetryPolicy
    {
        private readonly Core.RetryPolicy _inner;

        public int MaxRetries
        {
            get => _inner.MaxRetries;
            set => _inner.MaxRetries = value;
        }

        public int BaseDelayMs
        {
            get => _inner.BaseDelayMs;
            set => _inner.BaseDelayMs = value;
        }

        public bool UseExponentialBackoff
        {
            get => _inner.UseExponentialBackoff;
            set => _inner.UseExponentialBackoff = value;
        }

        public SideloadRetryPolicy()
        {
            _inner = new Core.RetryPolicy
            {
                MaxRetries = 3,
                BaseDelayMs = 500,
                UseExponentialBackoff = true
            };
        }

        public int GetDelayMs(int retryCount) => _inner.GetDelayMs(retryCount);
    }


}

