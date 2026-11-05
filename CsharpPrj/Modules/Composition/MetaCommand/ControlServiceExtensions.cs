using System;
using System.Collections.Generic;
using System.Linq;

namespace MetaCommand
{
    public static class ControlServiceExtensions
    {
        public static void SetItems(this IControlService service, object control, IEnumerable<string> items)
        {
            if (control == null) throw new ArgumentNullException(nameof(control));
            if (items == null) throw new ArgumentNullException(nameof(items));

            service.InvokeIfRequired(control, () =>
            {
                var type = control.GetType();

                type.GetMethod("BeginUpdate")?.Invoke(control, null);
                try
                {
                    var itemsCollection = type.GetProperty("Items")?.GetValue(control);
                    if (itemsCollection == null) return;

                    itemsCollection.GetType().GetMethod("Clear")?.Invoke(itemsCollection, null);

                    var addMethod = itemsCollection.GetType().GetMethod("Add", new[] { typeof(string) });
                    if (addMethod == null) return;

                    foreach (var item in items)
                        addMethod.Invoke(itemsCollection, new object[] { item });
                }
                finally
                {
                    type.GetMethod("EndUpdate")?.Invoke(control, null);
                }
            });
        }

        public static string GetPreviewValue(this IControlService service, object control)
        {
            try { return service.GetValue(control)?.ToString() ?? string.Empty; }
            catch { return string.Empty; }
        }

        public static void SetValues(this IControlService service, Dictionary<string, object?> values)
        {
            if (values == null) throw new ArgumentNullException(nameof(values));

            foreach (var kvp in values)
            {
                var control = service.FindByName(kvp.Key);
                if (control != null)
                {
                    service.SetValue(control, kvp.Value);
                }
            }
        }

        public static Dictionary<string, object?> GetValues(this IControlService service, IEnumerable<string> controlNames)
        {
            if (controlNames == null) throw new ArgumentNullException(nameof(controlNames));

            return controlNames
                .Select(name => new { Name = name, Control = service.FindByName(name) })
                .Where(x => x.Control != null)
                .ToDictionary(x => x.Name, x => service.GetValue(x.Control));
        }
    }
}
