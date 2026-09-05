
using System.Collections;
using System.Reflection;

namespace Modules.EventFramework.Utils;

public static class ReflectionChildControlProvider
{
    private static readonly string[] s_controlPropertyNames = new[]
    {
        "Panel1",
        "Panel2",
        "Controls",
        "Panel1Controls",
        "Panel2Controls",
    };

    public static IEnumerable<object> GetChildControls(object parent)
    {
        if (parent == null)
            throw new ArgumentNullException(nameof(parent));

        var type = parent.GetType();
        var childCount = 0;
        var foundAnyProperty = false;

        foreach (var propName in s_controlPropertyNames)
        {
            var prop = type.GetProperty(propName, BindingFlags.Public | BindingFlags.Instance);
            if (prop == null)
                continue;

            foundAnyProperty = true;

            var value = prop.GetValue(parent);
            if (value == null)
                continue;

            if (value is IEnumerable controls && value is not string)
            {
                foreach (var child in controls)
                {
                    childCount++;
                    yield return child;
                }
            }
            else if (value is not string)
            {
                childCount++;
                yield return value;
            }
        }

        if (!foundAnyProperty)
        {
            throw new InvalidOperationException(
                $"ReflectionChildControlProvider: 类型 '{type.Name}' 未找到任何已知的子控件属性。"
                + $"已查找属性: {string.Join(", ", s_controlPropertyNames)}。"
                + $"请检查该类型是否具有 Controls/Panel1Controls/Panel2Controls 属性。");
        }

    }

    public static List<object> GetChildControlsList(object parent)
    {
        return new List<object>(GetChildControls(parent));
    }
}
