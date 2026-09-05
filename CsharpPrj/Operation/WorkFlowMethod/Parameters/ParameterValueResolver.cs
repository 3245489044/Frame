
using System.Reflection;
using ParameterBinding = TConfig.ParameterBinding;
using WorkFlowMethod.Parameters.Abstractions;

namespace WorkFlowMethod.Parameters;

public class ParameterValueResolver : IParameterValueResolver
{
    public static readonly ParameterValueResolver Default = new();

    private readonly TConfig.Controls.IControlService _valueService;

    /// <summary>
    /// 阶段B（消费者抽象化）：值读取经 <see cref="TConfig.Controls.IControlService"/> 抽象，不直接触碰
    /// 静态 <c>ControlValueAccessor</c>；无注入时回退默认后端（<see cref="ControlValueBridge"/>）。
    /// </summary>
    public ParameterValueResolver()
        : this(new WinFormsValueContract.ControlValueBridge())
    {
    }

    public ParameterValueResolver(TConfig.Controls.IControlService valueService)
    {
        _valueService = valueService ?? throw new ArgumentNullException(nameof(valueService));
    }

    public object? ResolveValue(object? parentForm, ParameterBinding binding)
    {
        if (binding == null) return null;

        return binding.SourceType switch
        {
            "control" => ResolveControlValue(parentForm, binding),
            "literal" => ResolveLiteralValue(binding.Source),
            "expression" => ResolveExpressionValue(binding.Source),
            _ => ResolveLiteralValue(binding.Source)
        };
    }

    public object? ResolveControlValue(object? form, ParameterBinding binding)
    {
        if (form == null || binding == null) return null;

        var source = binding.Source;
        if (string.IsNullOrEmpty(source))
            return null;

        object? sourceControl = ResolveControlByPath(form, source);
        if (sourceControl == null)
            return null;

        if (!string.IsNullOrEmpty(binding.Property))
        {
            // 内核单点：属性路径解析（ReadNestedProperty/GetIndexedValue 唯一实现）
            return ParameterResolver.Utilities.PropertyPathResolver.ReadNestedProperty(
                sourceControl, binding.Property);
        }

        // 内核单点：控件取值（值语义契约，含 ListView 选中项特判）
        return _valueService.GetValue(sourceControl);
    }

    public object? ResolveLiteralValue(string source)
    {
        return source;
    }

    public object? ResolveExpressionValue(string expression, IDictionary<string, object>? variables = null)
    {
        if (string.IsNullOrEmpty(expression))
            return expression;

        if (expression.Contains("${"))
        {
            var resolved = System.Text.RegularExpressions.Regex.Replace(
                expression,
                @"\$\{(.+?)\}",
                match =>
                {
                    var key = match.Groups[1].Value;
                    var defaultValue = "";
                    var colonIndex = key.IndexOf(':');
                    if (colonIndex >= 0)
                    {
                        defaultValue = key[(colonIndex + 1)..];
                        key = key[..colonIndex];
                    }

                    if (variables != null && variables.TryGetValue(key, out var varValue) && varValue != null)
                    {
                        return varValue.ToString() ?? defaultValue;
                    }

                    if (!string.IsNullOrEmpty(defaultValue))
                        return defaultValue;

                    WorkFlowMethodLog.Log("ParameterValueResolver_PlaceholderNotFound",
                        new { expression, key });

                    return match.Value;
                });
            return resolved;
        }

        return expression;
    }


    public string? ReadListViewSelectedValue(object listView)
        => _valueService.GetValue(listView, TConfig.Controls.ValueSemantic.Selection)?.ToString();

    #region 私有辅助方法

    private static object? ResolveControlByPath(object root, string path)
    {
        if (root == null || string.IsNullOrEmpty(path))
            return null;

        var parts = path.Split('.');
        object? current = root;

        foreach (var part in parts)
        {
            if (current == null) return null;

            var controlsProp = current.GetType().GetProperty("Controls");
            if (controlsProp == null) return null;

            var controls = controlsProp.GetValue(current);
            if (controls == null) return null;

            var findMethod = controls.GetType().GetMethod("Find", new[] { typeof(string), typeof(bool) });
            if (findMethod == null)
            {
                var prop = current.GetType().GetProperty(part, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (prop == null) return null;
                current = prop.GetValue(current);
                continue;
            }

            var found = (Array)findMethod.Invoke(controls, new object[] { part, true })!;
            current = found.Length > 0 ? found.GetValue(0) : null;
        }

        return current;
    }

    #endregion
}
