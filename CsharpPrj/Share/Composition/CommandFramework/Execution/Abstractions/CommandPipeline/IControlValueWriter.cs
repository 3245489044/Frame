
namespace CommandFramework.Abstractions;

[Obsolete("Use TConfig.Controls.IControlValueAccessor instead. IControlValueAccessor 提供更完整的控件值读写能力。")]
public interface IControlValueWriter
{
    bool WriteValue(object control, string? value);

    bool WriteValue(object control, object? value);

    string? ReadValue(object control);

    bool SupportsType(object control);

    void RegisterAdapter<TControl>(
        Func<TControl, string?, bool> writer,
        Func<TControl, string?> reader) where TControl : class;
}
