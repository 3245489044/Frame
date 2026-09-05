
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using TConfig;

namespace BackEndDriven.Diagnostics
{
    public class EventBindingDiagnosticsService : IEventBindingDiagnostics
    {

        private int _totalBoundCount;
        private int _totalSkippedCount;
        private int _totalErrorCount;
        private EventBindingContext? _lastContext;
        private readonly object _lock = new();

        private readonly ConcurrentDictionary<string, ControlBindingInfo> _boundControls = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, ControlBindingInfo> _skippedControls = new(StringComparer.OrdinalIgnoreCase);


        public int TotalBoundCount
        {
            get { lock (_lock) return _totalBoundCount; }
        }

        public int TotalSkippedCount
        {
            get { lock (_lock) return _totalSkippedCount; }
        }

        public int TotalErrorCount
        {
            get { lock (_lock) return _totalErrorCount; }
        }

        public EventBindingContext? LastBindingContext
        {
            get { lock (_lock) return _lastContext; }
        }


        public void CaptureFromContext(EventBindingContext context)
        {
            if (context == null) return;

            lock (_lock)
            {
                _lastContext = context;

                _totalBoundCount += context.BoundCount;
                _totalSkippedCount += context.SkippedCount;
                _totalErrorCount += context.ErrorCollector?.Count ?? 0;

                foreach (var info in context.BoundControls)
                {
                    if (info?.ControlName != null)
                    {
                        _boundControls[info.ControlName] = info;
                    }
                }

                foreach (var info in context.SkippedControls)
                {
                    if (info?.ControlName != null)
                    {
                        _skippedControls[info.ControlName] = info;
                    }
                }
            }
        }

        public void RecordBinding(
            string controlName,
            string controlType,
            string status,
            string? errorMessage = null,
            string? skipReason = null)
        {
            if (string.IsNullOrWhiteSpace(controlName)) return;

            var info = new ControlBindingInfo
            {
                ControlName = controlName,
                ControlType = controlType ?? "",
                Status = status ?? "Bound",
                ErrorMessage = errorMessage,
                SkipReason = skipReason
            };

            lock (_lock)
            {
                switch (status?.ToLowerInvariant())
                {
                    case "bound":
                        _boundControls[controlName] = info;
                        _totalBoundCount++;
                        break;
                    case "skipped":
                        _skippedControls[controlName] = info;
                        _totalSkippedCount++;
                        break;
                    case "failed":
                        info.Status = "Failed";
                        info.SkipReason = info.SkipReason ?? info.ErrorMessage ?? "Failed to bind";
                        _skippedControls[controlName] = info;
                        _totalErrorCount++;
                        break;
                    default:
                        _boundControls[controlName] = info;
                        _totalBoundCount++;
                        break;
                }
            }
        }


        public ControlBindingInfo? GetControlBinding(string controlName)
        {
            if (string.IsNullOrWhiteSpace(controlName)) return null;

            if (_boundControls.TryGetValue(controlName, out var bound))
                return bound;

            if (_skippedControls.TryGetValue(controlName, out var skipped))
                return skipped;

            return null;
        }

        public bool IsControlBound(string controlName)
        {
            if (string.IsNullOrWhiteSpace(controlName)) return false;
            return _boundControls.ContainsKey(controlName);
        }

        public IReadOnlyList<ControlBindingInfo> GetBoundControls()
        {
            return _boundControls.Values.ToList().AsReadOnly();
        }

        public IReadOnlyList<ControlBindingInfo> GetSkippedControls()
        {
            return _skippedControls.Values.ToList().AsReadOnly();
        }

        public string GetDiagnosticSummary()
        {
            int bound, skipped, errors;
            lock (_lock)
            {
                bound = _totalBoundCount;
                skipped = _totalSkippedCount;
                errors = _totalErrorCount;
            }

            return $"Bound={bound}, Skipped={skipped}, Errors={errors}";
        }

        public void Reset()
        {
            lock (_lock)
            {
                _totalBoundCount = 0;
                _totalSkippedCount = 0;
                _totalErrorCount = 0;
                _lastContext = null;
                _boundControls.Clear();
                _skippedControls.Clear();
            }
        }
    }
}