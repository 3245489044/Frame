using System.Reflection;
using Modules.LayoutAnalysis.UIInspector;
using Modules.LayoutAnalysis.UIOverlapDetector.Calculator;
using Modules.LayoutAnalysis.UIOverlapDetector.Core;
using Modules.LayoutAnalysis.UIOverlapDetector.Models;
using Modules.LayoutAnalysis.UIOverlapDetector.Reporter;

namespace Modules.LayoutAnalysis.UIOverlapDetector.Monitor;

public class OverlapMonitor : IOverlapMonitor
{
    private readonly IOverlapCalculator _calculator;
    private readonly IOverlapReporter _reporter;
    private readonly System.Threading.Timer? _debounceTimer;
    private readonly object _lockObj = new();

    private object? _attachedForm;
    private ControlTreeWalker? _walker;
    private bool _disposed;
    private bool _debouncePending;
    private const int DebounceIntervalMs = 500;

    public event EventHandler<OverlapDetectedEventArgs>? OverlapDetected;

    public bool IsAttached => _attachedForm != null;

    public OverlapMonitor(
        IOverlapCalculator? calculator = null,
        IOverlapReporter? reporter = null)
    {
        _calculator = calculator ?? new OverlapCalculator();
        _reporter = reporter ?? new ConsoleOverlapReporter();
        _debounceTimer = new System.Threading.Timer(
            OnDebounceTimerElapsed,
            null,
            Timeout.Infinite,
            Timeout.Infinite);
    }

    public void Attach(object form, ControlTreeWalker walker)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(OverlapMonitor));

        Detach();

        _attachedForm = form ?? throw new ArgumentNullException(nameof(form));
        _walker = walker ?? throw new ArgumentNullException(nameof(walker));

        SubscribeFormEvents(form);

        CheckNow("Attach");
    }

    public void Detach()
    {
        if (_attachedForm != null)
        {
            UnsubscribeFormEvents(_attachedForm);
            _attachedForm = null;
            _walker = null;
        }
    }

    public OverlapAnalysisResult CheckNow()
    {
        return CheckNow("ManualCheck");
    }

    public OverlapAnalysisResult CheckNow(string source)
    {
        if (_attachedForm == null || _walker == null)
            return new OverlapAnalysisResult { TotalControls = 0 };

        try
        {
            var controls = _walker.Walk((System.Windows.Forms.Control)_attachedForm);

            var result = _calculator.Calculate(controls);

            if (result.HasProblematicOverlaps)
            {
                _reporter.Report(result);
            }

            OverlapDetected?.Invoke(this, new OverlapDetectedEventArgs(result, source));

            return result;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[OverlapMonitor] 检测失败: {ex.Message}");
            return new OverlapAnalysisResult { TotalControls = 0 };
        }
    }

    public void TriggerDebouncedCheck(string source = "LayoutChanged")
    {
        lock (_lockObj)
        {
            _debouncePending = true;
            _debounceTimer?.Change(DebounceIntervalMs, Timeout.Infinite);
        }
    }

    private void OnDebounceTimerElapsed(object? state)
    {
        lock (_lockObj)
        {
            if (!_debouncePending || _disposed)
                return;

            _debouncePending = false;
        }

        CheckNow("DebouncedLayoutChange");
    }


    private void SubscribeFormEvents(object form)
    {
        var formType = form.GetType();

        TrySubscribeEvent(form, formType, "Shown", (EventHandler)((s, e) =>
        {
            CheckNow("FormShown");
        }));

        TrySubscribeEvent(form, formType, "Resize", (EventHandler)((s, e) =>
        {
            TriggerDebouncedCheck("FormResize");
        }));

        TrySubscribeEvent(form, formType, "InitializationCompleted", 
            (EventHandler)((s, e) =>
            {
                CheckNow("InitializationCompleted");
            }));

        SubscribeConfigContainerEvents(form, formType);
    }

    private void SubscribeConfigContainerEvents(object form, Type formType)
    {
        try
        {
            var fields = formType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var properties = formType.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            foreach (var field in fields)
            {
                if (IsConfigContainerType(field.FieldType))
                {
                    var container = field.GetValue(form);
                    if (container != null)
                    {
                        SubscribeConfigLoaded(container);
                    }
                }
            }

            foreach (var prop in properties)
            {
                if (IsConfigContainerType(prop.PropertyType) && prop.CanRead)
                {
                    var container = prop.GetValue(form);
                    if (container != null)
                    {
                        SubscribeConfigLoaded(container);
                    }
                }
            }
        }
        catch
        {
        }
    }

    private static bool IsConfigContainerType(Type type)
    {
        return type.Name.Equals("ConfigContainer", StringComparison.Ordinal) ||
               type.Name.EndsWith("ConfigContainer", StringComparison.Ordinal);
    }

    private void SubscribeConfigLoaded(object container)
    {
        var containerType = container.GetType();
        var configLoadedEvent = containerType.GetEvent("ConfigLoaded");
        if (configLoadedEvent != null)
        {
            var handler = CreateConfigLoadedHandler(container);
            if (handler != null)
            {
                configLoadedEvent.AddEventHandler(container, handler);
                _configLoadedHandlers[container] = handler;
            }
        }
    }

    private Delegate? CreateConfigLoadedHandler(object container)
    {
        var containerType = container.GetType();
        var eventArgsType = containerType.Assembly.GetType(
            containerType.Namespace + ".ContainerConfigLoadedEventArgs");
        
        if (eventArgsType != null)
        {
            var handlerType = typeof(EventHandler<>).MakeGenericType(eventArgsType);
            var method = GetType().GetMethod("OnConfigLoadedGeneric", 
                BindingFlags.NonPublic | BindingFlags.Instance);
            if (method != null)
            {
                var genericMethod = method.MakeGenericMethod(eventArgsType);
                return Delegate.CreateDelegate(handlerType, this, genericMethod);
            }
        }

        EventHandler fallbackHandler = (s, e) =>
        {
            CheckNow("ConfigLoaded");
        };
        return fallbackHandler;
    }

    private void OnConfigLoadedGeneric<TEventArgs>(object? sender, TEventArgs e)
    {
        CheckNow("ConfigLoaded");
    }

    private static void TrySubscribeEvent(object target, Type targetType, string eventName, Delegate handler)
    {
        try
        {
            var eventInfo = targetType.GetEvent(eventName, 
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (eventInfo != null)
            {
                eventInfo.AddEventHandler(target, handler);
            }
        }
        catch
        {
        }
    }


    private readonly Dictionary<object, Delegate> _configLoadedHandlers = new();

    private void UnsubscribeFormEvents(object form)
    {
        var formType = form.GetType();

        foreach (var kvp in _configLoadedHandlers)
        {
            try
            {
                var containerType = kvp.Key.GetType();
                var configLoadedEvent = containerType.GetEvent("ConfigLoaded");
                configLoadedEvent?.RemoveEventHandler(kvp.Key, kvp.Value);
            }
            catch { }
        }
        _configLoadedHandlers.Clear();
    }


    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Detach();
        _debounceTimer?.Dispose();

        GC.SuppressFinalize(this);
    }
}
