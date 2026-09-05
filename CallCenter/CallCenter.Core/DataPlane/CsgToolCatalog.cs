using CSG.Unified;

namespace CallCenter.Core.DataPlane;

/// <summary>
/// CSG 数据面工具目录：把注册的工具（<see cref="Tools.ToolDescriptor"/>）投影为一张
/// <see cref="CanonicalForm"/>（配置/Graph 投影）——每个工具一个 <see cref="CanonicalNode"/>，
/// 签名/fingerprint/backend/flags 进节点属性与 metadata。控制面（Router）只据此 CanonicalForm 路由。
/// </summary>
public sealed class CsgToolCatalog
{
    private readonly CsgFacade _csg;

    private readonly List<Tools.ToolDescriptor> _tools = new();

    public CsgToolCatalog(CsgFacade csg)
    {
        _csg = csg;
    }

    public IReadOnlyList<Tools.ToolDescriptor> Tools => _tools;

    /// <summary>
    /// 登记一个工具。Id（<c>tool:{backend}:{name}</c>）在目录内必须唯一；重复登记视为「重名/冲突」，
    /// 抛 <see cref="InvalidOperationException"/>（P0 闸：重名检测）。同名多实现（不同 backend/version）
    /// 是多轨并存，靠不同 Id 合法共存，不受此约束。
    /// </summary>
    public void Register(Tools.ToolDescriptor tool)
    {
        ArgumentNullException.ThrowIfNull(tool);

        if (_tools.Any(t => string.Equals(t.Id, tool.Id, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                $"工具 '{tool.Id}' 在目录中被重复登记。工具 Id（tool:{{backend}}:{{name}}）必须全局唯一，" +
                "如需同名多实现请改用不同 backend/version（多轨并存）。");
        }

        _tools.Add(tool);
    }

    public void RegisterRange(IEnumerable<Tools.ToolDescriptor> tools)
    {
        ArgumentNullException.ThrowIfNull(tools);
        foreach (var t in tools) Register(t);
    }

    /// <summary>工具表 → CanonicalForm（每个工具一个 Action 节点，签名/flags 进属性）。</summary>
    public CanonicalForm ToCanonical()
    {
        var form = new CanonicalForm();

        foreach (var t in _tools)
        {
            var node = new CanonicalNode
            {
                Id = t.Id,
                Label = t.Name,
                Type = CanonicalNodeType.Action,
                Properties = new Dictionary<string, object?>
                {
                    ["backend"] = t.Backend,
                    ["version"] = t.Version,
                    ["fingerprint"] = t.Fingerprint,
                    ["description"] = t.Description,
                    ["inputs"] = t.InputSchema,
                    ["outputs"] = t.OutputSchema,
                    ["flags"] = t.Flags.ToString(),
                },
            };
            form.Nodes.Add(node);
        }

        form.Metadata["schema"] = "csg://tool-catalog";
        form.Metadata["kind"] = "tool-catalog";
        return form;
    }

    /// <summary>按 CSG 校验规则验正工具表图。</summary>
    public ValidationResult Validate() => _csg.Validate(ToCanonical());
}