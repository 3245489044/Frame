using System.Collections;
using System.Reflection;

namespace ParameterResolver.Utilities;

/// <summary>
/// 属性路径解析内核（单点实现）——收敛散落在 BackEndDriven.ParameterBinder / WorkFlowMethod.ParameterBuilder /
/// WorkFlowMethod.ParameterValueResolver 等处的「按点路径读取嵌套属性（含数字索引段）」重复实现。
/// 纯反射 + IList 索引，不依赖具体控件类型；未命中/越界一律返回 null（调用方决定降级策略）。
/// </summary>
public static class PropertyPathResolver
{
    /// <summary>按点分路径读取嵌套属性值：`SelectedItems.0.Tag` 表示先取 SelectedItems 属性、再取索引 0、再取 Tag。</summary>
    public static object? ReadNestedProperty(object root, string propertyPath)
    {
        if (root == null || string.IsNullOrEmpty(propertyPath))
            return null;

        object? current = root;
        var parts = propertyPath.Split('.');

        foreach (var part in parts)
        {
            if (current == null) return null;

            if (int.TryParse(part, out var index))
            {
                current = GetIndexedValue(current, index);
                continue;
            }

            var prop = current.GetType().GetProperty(
                part,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (prop == null || !prop.CanRead)
                return null;

            current = prop.GetValue(current);
        }

        return current;
    }

    /// <summary>索引器取值：IList 优先（含边界检查），其次 Item 索引器属性，最后 get_Item 方法。失败返回 null。</summary>
    public static object? GetIndexedValue(object collection, int index)
    {
        try
        {
            if (collection is IList list)
            {
                return index >= 0 && index < list.Count ? list[index] : null;
            }

            var itemProp = collection.GetType().GetProperty("Item");
            if (itemProp != null && itemProp.CanRead)
            {
                return index >= 0 ? itemProp.GetValue(collection, new object[] { index }) : null;
            }

            var getItemMethod = collection.GetType().GetMethod("get_Item");
            return getItemMethod?.Invoke(collection, new object[] { index });
        }
        catch
        {
            return null;
        }
    }
}
