using FrontEndDriven.Diagnostics;
using Ports;

namespace FrontEndDriven.Implementation;

public class CompositeValueWriter : TConfig.Controls.IControlValueAccessor
{
    private readonly List<TConfig.Controls.IControlValueAccessor> _writers = new();

    public void AddWriter(TConfig.Controls.IControlValueAccessor writer)
    {
        if (writer == null) throw new ArgumentNullException(nameof(writer));
        _writers.Add(writer);
    }

    public bool RemoveWriter(TConfig.Controls.IControlValueAccessor writer)
    {
        return _writers.Remove(writer);
    }

    public void ClearWriters()
    {
        _writers.Clear();
    }

    public int WriterCount => _writers.Count;

    private TConfig.Controls.IControlValueAccessor? FindFirstSupportedWriter(object control)
    {
        foreach (var writer in _writers)
        {
            if (writer.IsSupported(control))
                return writer;
        }
        return null;
    }

    public object? GetValue(object control)
    {
        if (control == null)
            return null;

        var writer = FindFirstSupportedWriter(control);
        return writer?.GetValue(control);
    }

    public void SetValue(object control, object? value)
    {
        if (control == null)
            return;

        var writer = FindFirstSupportedWriter(control);
        if (writer != null)
        {
            writer.SetValue(control, value);
            return;
        }

        // 无任何 writer 支持该控件：与 GetValue 的返回 null 语义对齐为"不可读写"，记录诊断日志而非对外抛异常
        FrontEndDrivenLog.Log("CompositeValueWriter_SetValue_no_supported_writer",
            new { controlType = control.GetType().Name });
    }

    public bool IsSupported(object control)
    {
        if (control == null)
            return false;

        return FindFirstSupportedWriter(control) != null;
    }

    public void RegisterAdapter(Type controlType, Func<object, object?> getter, Action<object, object?> setter)
    {
        if (controlType == null) throw new ArgumentNullException(nameof(controlType));
        if (getter == null) throw new ArgumentNullException(nameof(getter));
        if (setter == null) throw new ArgumentNullException(nameof(setter));

        // 广播到所有 writer：消除"读取按添加序从前到后、写入却只到末位 writer"的方向矛盾。
        // 语义改为"将该 adapter 声明给全部内部 IControlValueAccessor"，与 IsSupported/GetValue/SetValue 的
        // 逐 writer 尝试保持一致，避免注册到末位 writer 却不被读取路径命中。
        foreach (var writer in _writers)
        {
            writer.RegisterAdapter(controlType, getter, setter);
        }
    }

    public void ClearAdapters()
    {
        foreach (var w in _writers)
        {
            w.ClearAdapters();
        }
    }
}