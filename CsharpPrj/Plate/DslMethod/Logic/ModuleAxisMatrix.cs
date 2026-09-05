using Plate.DslMethod.Model;

namespace Plate.DslMethod.Logic;

/// <summary>模块×轴矩阵单元坐标。</summary>
public sealed record MatrixCell(string Module, string Axis);

/// <summary>
/// 模块 × 轴 对应矩阵：把 [Bind] 绑定按 (module, axis) 分桶，供导航与缺口分析。
/// </summary>
public sealed class ModuleAxisMatrix
{
    private readonly string[] _modules;
    private readonly string[] _axes;
    private readonly Dictionary<string, List<BindingDeclaration>> _cells = new(StringComparer.OrdinalIgnoreCase);

    public ModuleAxisMatrix(string[] modules, string[] axes)
    {
        _modules = modules;
        _axes = axes;
    }

    public string[] Modules => _modules;
    public string[] Axes => _axes;

    public void Add(string module, string axis, BindingDeclaration b)
    {
        if (string.IsNullOrEmpty(module) || string.IsNullOrEmpty(axis)) return;
        _cells.GetOrAdd($"{module}|{axis}").Add(b);
    }

    public IReadOnlyList<BindingDeclaration> Cell(string module, string axis)
        => _cells.TryGetValue($"{module}|{axis}", out var list) ? list : Array.Empty<BindingDeclaration>();

    public bool HasCell(string module, string axis) => _cells.ContainsKey($"{module}|{axis}");

    /// <summary>空单元格（该模块在该轴上无绑定）→ 缺口候选。</summary>
    public IEnumerable<MatrixCell> EmptyCells()
    {
        foreach (var m in _modules)
            foreach (var a in _axes)
                if (!_cells.ContainsKey($"{m}|{a}"))
                    yield return new MatrixCell(m, a);
    }
}
