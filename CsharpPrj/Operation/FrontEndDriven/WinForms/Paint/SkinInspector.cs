#nullable enable

using System.Windows.Forms;
using FrameworkAbstractions.Skinning;
using FrameworkAbstractions.WinFormsAbstractions;
using Share.Conversation.Rendering.Surface;

namespace FrontEndDriven.WinForms.Paint;

/// <summary>
/// <see cref="ISkinInspector"/> 的唯一实现：把绑定快照与像素探针暴露给命令层（不泄漏绘制细节）。
/// </summary>
public sealed class SkinInspector : ISkinInspector
{
    private readonly IControlFinder? _controlFinder;

    /// <summary>按控件查找器构造（null ⇒ 像素判定恒失败并给出原因，禁静默通过）。</summary>
    /// <param name="controlFinder">控件查找器（与 validate/get 同一寻址口径）。</param>
    public SkinInspector(IControlFinder? controlFinder) => _controlFinder = controlFinder;

    /// <inheritdoc />
    public string DescribeBinding() => SkinDiagnostics.Describe();

    /// <inheritdoc />
    public bool TryGetBinding(string key, out string value)
    {
        var snapshot = SkinDiagnostics.Current;
        value = key switch
        {
            "enabled" => snapshot.Enabled ? "true" : "false",
            "profile" => snapshot.Profile,
            "carrier" => snapshot.Carrier,
            "catalog" or "catalogcount" => snapshot.CatalogCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "layers" or "globallayers" => snapshot.GlobalLayerCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "unresolvedtokens" or "unresolved" => snapshot.UnresolvedTokenCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "attached" => snapshot.AttachedControls.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "painted" => snapshot.PaintCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "global" or "material" => snapshot.GlobalMaterial,
            "fingerprint" => snapshot.Fingerprint,
            _ => null,
        };

        value ??= string.Empty;
        return value.Length > 0 || key is "profile" or "carrier" or "global" or "material" or "fingerprint";
    }

    /// <inheritdoc />
    public string DescribeUnresolved() => SkinDiagnostics.DescribeUnresolved();

    /// <inheritdoc />
    public bool TryInspectPixels(string controlName, double minDelta, int tolerance, out string evidence)
    {
        if (_controlFinder is null)
        {
            evidence = "IControlFinder 未注册（控件寻址不可用）";
            return false;
        }

        var control = _controlFinder.FindByName<Control>(controlName);
        if (control is null)
        {
            evidence = $"未找到控件 '{controlName}'";
            return false;
        }

        if (!SkinDecorator.IsOwnerDrawSupported(control))
        {
            evidence = $"控件 '{controlName}' 类型 {control.GetType().Name} 不在 owner-draw 白名单（永不换肤）";
            return false;
        }

        var material = SkinDecorator.MaterialOf(control);
        if (material is null)
        {
            evidence = $"控件 '{controlName}' 未挂载材质（{SkinDiagnostics.Describe()}）";
            return false;
        }

        var report = SkinPixelProbe.Inspect(control, material, tolerance);
        evidence = report.Describe();
        var unresolved = SkinDiagnostics.DescribeUnresolved();

        if (report.Samples.Count == 0)
        {
            evidence += " 无法采样（尺寸为 0 或绘制失败）";
            return false;
        }

        if (report.MaxDelta < minDelta)
        {
            evidence += $" 材质未落像素（maxDelta={report.MaxDelta:0.0} < {minDelta:0.0}；疑似 token 未换值或开关关闭）"
                        + (unresolved.Length > 0 ? " " + unresolved : string.Empty);
            return false;
        }

        if (report.Matched == 0)
        {
            evidence += $" 像素与解析材质不符（无采样点落在容差 {tolerance} 内）";
            return false;
        }

        return true;
    }
}
