
using System.Windows.Forms;
using Microsoft.Extensions.Logging;
using Modules.EventFramework.Abstractions;
using TConfig;

namespace BackEndDriven.IoC;

public class ParameterBinder : IParameterBinder
{
    private readonly ILogger<ParameterBinder>? _logger;

    public ParameterBinder(ILogger<ParameterBinder>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// D6 收敛：从绑定声明（配置层 TConfig.ParameterBinding）直接构建参数字典——
    /// 废除 PrecomputeSources/ParameterSource 预计算层（参数绑定描述随声明持有，触发时一次求值）。
    /// </summary>
    public Dictionary<string, object> Build(
        IReadOnlyList<ParameterBinding>? bindings,
        Form? parentForm,
        object sender)
    {
        if (bindings == null || bindings.Count == 0)
        {
            var value = (sender as System.Windows.Forms.Control)?.Text ?? "";
            return new Dictionary<string, object> { ["value"] = value };
        }

        var result = new Dictionary<string, object>(bindings.Count);
        for (int i = 0; i < bindings.Count; i++)
        {
            var binding = bindings[i];
            var sourceType = binding.SourceType ?? "control";
            var sourceName = binding.Source ?? "";
            var sourceProperty = binding.Property ?? "Text";
            var targetName = binding.Target ?? binding.Source ?? $"param{i}";

            object? value = null;

            if (string.Equals(sourceType, "literal", StringComparison.OrdinalIgnoreCase))
            {
                value = sourceName;
            }
            else
            {
                System.Windows.Forms.Control? sourceControl;

                if (string.IsNullOrEmpty(sourceName))
                {
                    sourceControl = sender as System.Windows.Forms.Control;
                }
                else
                {
                    sourceControl = parentForm?.Controls
                        .Find(sourceName, true)
                        .FirstOrDefault();
                }

                if (sourceControl != null && !string.IsNullOrEmpty(sourceProperty))
                {
                    // 内核单点：属性路径解析（ReadNestedProperty/GetIndexedValue 唯一实现）
                    value = ParameterResolver.Utilities.PropertyPathResolver.ReadNestedProperty(
                        sourceControl, sourceProperty);
                }
            }

            if (value == null && binding.DefaultValue != null)
            {
                value = binding.DefaultValue;
            }

            // P1 修复：绑定失败不再静默吞掉——来源指定但未取到值且无默认值时记录 Warning（参数名+来源+属性路径），
            // 消除「configPath 空但无任何告警」的不可诊断故障。
            if (value == null && binding.DefaultValue == null
                && !string.IsNullOrEmpty(sourceName))
            {
                _logger?.LogWarning(
                    "参数绑定失败（空值且无默认值）: 目标参数 '{Target}'，来源控件 '{Source}'，属性路径 '{Property}'（sourceType={SourceType}）",
                    targetName, sourceName, sourceProperty, sourceType);
            }

            result[targetName] = value ?? "";
        }

        return result;
    }
}
