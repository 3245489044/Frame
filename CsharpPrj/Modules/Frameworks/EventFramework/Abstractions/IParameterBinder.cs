
using System.Windows.Forms;
using TConfig;

namespace Modules.EventFramework.Abstractions;

/// <summary>
/// 事件参数绑定器（D6 收敛后单一面）：绑定声明持有配置层参数 DTO（TConfig.ParameterBinding），
/// 触发时直接构建参数字典——废除 PrecomputeSources/ParameterSource 预计算层。
/// </summary>
public interface IParameterBinder
{
    Dictionary<string, object> Build(
        IReadOnlyList<ParameterBinding>? bindings,
        Form? parentForm,
        object sender);
}
