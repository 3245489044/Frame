#nullable enable

using System.Collections.Generic;
using System.Drawing;

namespace Modules.LayoutAnalysis.UIInspector
{
    public class ReflectionExtractor
    {
        public T? Extract<T>(object target, string propertyName)
        {
            if (target == null) return default;

            var type = target.GetType();
            var prop = type.GetProperty(propertyName);
            if (prop != null && prop.CanRead)
            {
                return TryConvert<T>(prop.GetValue(target));
            }

            var field = type.GetField(propertyName);
            if (field != null)
            {
                return TryConvert<T>(field.GetValue(target));
            }

            return default;
        }

        private static T? TryConvert<T>(object? value)
        {
            if (value == null) return default;
            if (value is T typed) return typed;

            var targetType = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
            try
            {
                if (targetType.IsEnum && value is string enumText)
                    return (T?)Enum.Parse(targetType, enumText, ignoreCase: true);
                if (targetType.IsInstanceOfType(value))
                    return (T?)value;
                return (T?)Convert.ChangeType(value, targetType, System.Globalization.CultureInfo.InvariantCulture);
            }
            catch (Exception ex) when (ex is InvalidCastException or FormatException or OverflowException)
            {
                return default;
            }
        }

        public Point ExtractLocation(object control)
        {
            return Extract<Point>(control, "Location");
        }

        public Size ExtractSize(object control)
        {
            return Extract<Size>(control, "Size");
        }

        public string? ExtractName(object control)
        {
            var name = Extract<string>(control, "Name");
            return string.IsNullOrEmpty(name) ? null : name;
        }

        public string? ExtractText(object control)
        {
            return Extract<string>(control, "Text");
        }

        public bool ExtractHasChildren(object control)
        {
            return Extract<bool>(control, "HasChildren");
        }

        public Dictionary<string, object?> ExtractAllProperties(object target)
        {
            var result = new Dictionary<string, object?>();
            if (target == null) return result;

            var type = target.GetType();
            foreach (var prop in type.GetProperties())
            {
                if (prop.CanRead)
                {
                    result[prop.Name] = prop.GetValue(target);
                }
            }
            foreach (var field in type.GetFields())
            {
                if (!result.ContainsKey(field.Name))
                {
                    result[field.Name] = field.GetValue(target);
                }
            }

            return result;
        }

        public ControlMetadata ExtractControlMetadata(object control, string parentName = "", int parentScreenLeft = 0, int parentScreenTop = 0)
        {
            var location = ExtractLocation(control);
            var size = ExtractSize(control);

            return new ControlMetadata
            {
                Name = ExtractName(control) ?? control.GetType().Name,
                TypeName = control.GetType().Name,
                Left = location.X,
                Top = location.Y,
                Width = size.Width,
                Height = size.Height,
                ScreenLeft = parentScreenLeft + location.X,
                ScreenTop = parentScreenTop + location.Y,
                Text = ExtractText(control) ?? string.Empty,
                ParentName = parentName,
                HasChildren = ExtractHasChildren(control)
            };
        }
    }
}
