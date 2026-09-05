
#nullable enable
using Share.TaskStatusPool.Core;
using TConfig.Controls;

namespace Share.TaskStatusPool.ControlPool
{
    public class ControlPool : ReadWritePool<string, object?>
    {
        public ControlPool() : base("ControlPool", StringComparer.OrdinalIgnoreCase)
        {
        }


        public void SyncFromControl(string controlName, object control, IControlValueAccessor accessor)
        {
            ArgumentNullException.ThrowIfNull(controlName);
            ArgumentNullException.ThrowIfNull(control);
            ArgumentNullException.ThrowIfNull(accessor);

            var value = accessor.GetValue(control);
            Write(controlName, value);
        }

        public void SyncToControl(string controlName, object control, IControlValueAccessor accessor)
        {
            ArgumentNullException.ThrowIfNull(controlName);
            ArgumentNullException.ThrowIfNull(control);
            ArgumentNullException.ThrowIfNull(accessor);

            var value = Read(controlName);
            if (value != null)
                accessor.SetValue(control, value?.ToString());
        }

        public void SyncToControls(Dictionary<string, object> controlMap, IControlValueAccessor accessor)
        {
            ArgumentNullException.ThrowIfNull(controlMap);
            ArgumentNullException.ThrowIfNull(accessor);

            foreach (var kvp in controlMap)
            {
                SyncToControl(kvp.Key, kvp.Value, accessor);
            }
        }

        public T? GetValue<T>(string controlName)
        {
            var value = Read(controlName);
            if (value is T typedValue)
                return typedValue;
            return default;
        }

        public void SetValue(string controlName, object? value)
        {
            Write(controlName, value);
        }

        public override string ToString()
            => $"ControlPool[{Name}] Count={Count}";
    }
}
