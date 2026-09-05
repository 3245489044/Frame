using Plate.DslMethod.Model;

namespace Plate.DslMethod.Logic;

/// <summary>
/// 契约注册中心（[Bind] 对应关系逻辑层）。
/// 职责：装载文档 → 建索引 → 提取 [Bind] 绑定 → 校验（悬空/缺项/非法枚举）→ 缺口与矩阵分析。
/// 解决"实现↔公理对应散落"：全部对应以 [Bind] 声明集中登记，可机检、可反向引用、可治理。
/// </summary>
public sealed class ContractRegistry
{
    private readonly DslIndex _index = new();
    private readonly List<DslDocument> _documents = new();
    private readonly List<BindingDeclaration> _bindings = new();

    public DslIndex Index => _index;
    public IReadOnlyList<DslDocument> Documents => _documents;
    public IReadOnlyList<BindingDeclaration> Bindings => _bindings;

    /// <summary>装载一组文档。全部文档先入索引，再统一提取绑定（保证 target 解析可见全索引）。</summary>
    public void Load(IEnumerable<DslDocument> documents)
    {
        foreach (var d in documents)
        {
            _documents.Add(d);
            _index.AddDocument(d);
        }
        foreach (var d in _documents)
            foreach (var decl in d.Declarations)
                if (decl.Kind == DslSymbolKind.Bind)
                    _bindings.Add(ToBinding(decl));
    }

    private static BindingDeclaration ToBinding(DslDeclaration d)
    {
        var targetRef = d.References.FirstOrDefault();
        return new BindingDeclaration
        {
            Name = d.Name,
            Kind = d.Field("kind"),
            Impl = d.Field("impl"),
            Target = targetRef,
            Module = d.Field("module"),
            Axis = d.Field("axis"),
            Layer = d.Field("layer"),
            Status = d.Field("status"),
            SourceFile = d.SourceFile,
            LineNumber = d.LineNumber,
        };
    }

    /// <summary>校验全部绑定：target 缺失/悬空、impl 缺失、kind/status 非法枚举。</summary>
    public List<ValidationIssue> Validate()
    {
        var issues = new List<ValidationIssue>();
        foreach (var b in _bindings)
        {
            if (b.Target is null)
            {
                issues.Add(new ValidationIssue(b, "缺少 target=[CR:...] 目标公理"));
            }
            else if (!_index.IsDefined(b.Target))
            {
                issues.Add(new ValidationIssue(b, $"target 悬空（索引不可解析）: {b.Target.Raw}"));
            }

            if (string.IsNullOrWhiteSpace(b.Impl))
                issues.Add(new ValidationIssue(b, "缺少 impl 实现符号"));

            if (!string.IsNullOrWhiteSpace(b.Kind) &&
                !ContractDimensions.Kinds.Contains(b.Kind, StringComparer.OrdinalIgnoreCase))
                issues.Add(new ValidationIssue(b, $"kind 非法: {b.Kind}（允许 {string.Join("/", ContractDimensions.Kinds)}）"));

            if (!string.IsNullOrWhiteSpace(b.Status) &&
                !ContractDimensions.Statuses.Contains(b.Status, StringComparer.OrdinalIgnoreCase))
                issues.Add(new ValidationIssue(b, $"status 非法: {b.Status}（允许 {string.Join("/", ContractDimensions.Statuses)}）"));
        }
        return issues;
    }

    /// <summary>按字段（module/axis/status/kind）分组绑定。</summary>
    public IReadOnlyDictionary<string, List<BindingDeclaration>> GroupBy(string field)
    {
        var map = new Dictionary<string, List<BindingDeclaration>>(StringComparer.OrdinalIgnoreCase);
        foreach (var b in _bindings)
        {
            var key = field.ToLowerInvariant() switch
            {
                "module" => b.Module,
                "axis" => b.Axis,
                "status" => b.Status,
                "kind" => b.Kind,
                _ => b.Module,
            };
            map.GetOrAdd(key).Add(b);
        }
        return map;
    }

    /// <summary>构建模块×轴矩阵（仅已注册维度）。</summary>
    public ModuleAxisMatrix BuildMatrix()
    {
        var m = new ModuleAxisMatrix(ContractDimensions.Modules, ContractDimensions.Axes);
        foreach (var b in _bindings)
            m.Add(b.Module, b.Axis, b);
        return m;
    }

    /// <summary>缺口：矩阵空单元格（该模块在该轴上无绑定）。派生为 status=gap 的占位绑定。</summary>
    public List<BindingDeclaration> FindGaps()
    {
        return BuildMatrix()
            .EmptyCells()
            .Select(c => new BindingDeclaration
            {
                Name = $"[gap] {c.Module}/{c.Axis}",
                Kind = "gap",
                Module = c.Module,
                Axis = c.Axis,
                Status = "gap",
                SourceFile = "(派生)",
            })
            .ToList();
    }

    /// <summary>漂移/缺口绑定（status != aligned）清单。</summary>
    public List<BindingDeclaration> FindByStatus(params string[] statuses)
        => _bindings.Where(b => statuses.Contains(b.Status, StringComparer.OrdinalIgnoreCase)).ToList();
}
