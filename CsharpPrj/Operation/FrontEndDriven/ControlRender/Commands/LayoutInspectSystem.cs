
using System.Text;
using System.Windows.Forms;
using ECS.Core;
using ECS.Bridge;
using ECS.Components;
using FrameworkAbstractions.ComponentRegistry;
using FrontEndDriven.Container;
using FrontEndDriven.ECS.Components;
using TConfig.ECS.Components;
using FrontEndDriven.Diagnostics;
using FrontEndDriven.ControlLookup;
using FrontEndDriven.Finder;
using Modules.LayoutAnalysis.UIInspector;
using Modules.LayoutAnalysis.UIOverlapDetector.Calculator;
using Modules.LayoutAnalysis.UIOverlapDetector.Models;

namespace FrontEndDriven.ControlRender.Commands;

public class LayoutInspectSystem : CommandSystemBase
{
    public LayoutInspectSystem(EntityManager entityManager)
        : base(entityManager, "layout") { }

    public override void Execute(Entity entity, CommandMetaComponent meta)
    {
        var subCommand = (meta.Target ?? "").ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(subCommand))
        {
            SetFailure(entity, "layout: 缺少子命令（positions/report/json-report/overlaps）");
            return;
        }

        try
        {
            switch (subCommand)
            {
                case "positions":
                    ExecutePositions(entity);
                    break;
                case "report":
                    ExecuteReport(entity);
                    break;
                case "json-report":
                    ExecuteJsonReport(entity);
                    break;
                case "overlaps":
                    ExecuteOverlaps(entity);
                    break;
                default:
                    SetFailure(entity, $"layout: 未知子命令 '{subCommand}'（支持: positions/report/json-report/overlaps）");
                    break;
            }
        }
        catch (Exception ex)
        {
            SetFailure(entity, $"layout '{subCommand}' 失败: {ex.Message}");
        }
    }

    private void ExecutePositions(Entity entity)
    {
        // M6·ECS 口径：经 EntityQueryService 取渲染世界（ECS 真源）读 ComputedLayoutComponent，
        // + ControlPool 物理投影校验。命令侧 EntityManager 为空世界（渲染实体在渲染 ECS），不可直接遍历。
        var queryService = AppServiceProvider.Resolve<EntityQueryService>();
        if (queryService == null)
        {
            SetFailure(entity, "layout positions: EntityQueryService 未注册（FrontEndDriven 管线未装配）");
            return;
        }

        var world = queryService.EntityManager;
        var controlPool = AppServiceProvider.Resolve<ControlPool>();
        // E-1（8.6）：scope root 物理控件缓存（scope → 控件），避免子 scope 多控件重复解析
        var scopeRootCache = new Dictionary<string, Control?>(StringComparer.OrdinalIgnoreCase);
        var sb = new StringBuilder();
        sb.AppendLine("--- 控件位置列表（ECS ComputedLayout / 物理校验） ---");

        int count = 0;
        foreach (var e in world.GetEntities<ControlTypeComponent>())
        {
            if (world.HasComponent<SplitterPanelComponent>(e))
                continue;

            if (!world.TryGetComponent<ComputedLayoutComponent>(e, out var computed))
                continue;

            var name = world.TryGetComponent<ControlInfoComponent>(e, out var info)
                ? info.Name
                : string.Empty;
            var entityScope = world.TryGetComponent<ScopeComponent>(e, out var sc)
                ? sc.Scope
                : string.Empty;

            string physical = "<物理未创建>";
            string mismatch = string.Empty;
            string basis = "form";
            var control = controlPool?.GetControl(e);
            if (control != null)
            {
                // E-1（8.6）修复：ECS ComputedLayoutComponent 对子 scope 为 scope 局部基准（宿主客户区起算，
                // LayoutSystem.TryGetParentContainerSize 对 HostBasis 父返回 parentLeft/Top=0），
                // 物理对照统一换算为 scope 局部基准（form 相对 − scope root form 相对）后同基准比对，
                // 消除「子 scope 控件恒偏移 root 在 form 中的偏移（如工具栏高度 43）」的 ECS_MISMATCH 伪影。
                // 根 scope / 无 scope 控件：scope root 即自身，偏移 (0,0)，行为不变。
                var physicalRect = LayoutCoordinateResolver.ToFormRelativeAccurate(control);
                int localLeft = physicalRect.Left;
                int localTop = physicalRect.Top;

                var scopeRootControl = ResolveScopeRootControl(queryService, controlPool, entityScope, scopeRootCache);
                if (scopeRootControl != null)
                {
                    var rootForm = LayoutCoordinateResolver.ToFormRelativeAccurate(scopeRootControl);
                    localLeft -= rootForm.Left;
                    localTop -= rootForm.Top;
                    basis = $"scope:{entityScope}";
                }
                else if (!string.IsNullOrEmpty(entityScope))
                {
                    basis = $"scope:{entityScope}(root未解析,form基准)";
                }

                physical = $"({localLeft},{localTop}) Size=({control.Width},{control.Height}) 基准={basis}";
                if (Math.Abs(localLeft - computed.Left) > 2
                    || Math.Abs(localTop - computed.Top) > 2
                    || Math.Abs(control.Width - computed.Width) > 2
                    || Math.Abs(control.Height - computed.Height) > 2)
                {
                    mismatch = " ← ECS_MISMATCH";
                }
            }

            sb.AppendLine($"  {name}: ECS=({computed.Left},{computed.Top},{computed.Width}x{computed.Height}) 物理={physical} scope={entityScope}{mismatch}");
            count++;
        }

        sb.AppendLine($"--- 总计: {count} 个控件（ECS 口径） ---");

        var result = sb.ToString();
        FrontEndDrivenLog.Log("Layout_Positions", new { count, detail = result });
        SetSuccess(entity, result);
    }

    /// <summary>
    /// 解析 scope root 物理控件（E-1 物理对照 scope 局部基准）：
    /// 优先经实体路径（EntityQueryService.FindRoot → ControlPool.GetControl(entity)，防跨 scope 同名按名误中）；
    /// 兜底按 root:{scope} 名查（root 控件可能未经实体→控件映射注册，与 RenderStageRunner.DestroyScopeEntities 同口径）。
    /// 结果缓存于 <paramref name="cache"/>（同 scope 多子控件复用）。
    /// </summary>
    private static Control? ResolveScopeRootControl(
        EntityQueryService? queryService,
        ControlPool? controlPool,
        string scope,
        IDictionary<string, Control?> cache)
    {
        if (controlPool == null || string.IsNullOrWhiteSpace(scope))
            return null;

        if (cache.TryGetValue(scope, out var cached))
            return cached;

        Control? rootControl = null;
        var rootEntity = queryService?.FindRoot(scope);
        if (rootEntity != null && rootEntity.Value.IsValid)
        {
            rootControl = controlPool.GetControl(rootEntity.Value);
        }

        rootControl ??= controlPool.GetControl($"root:{scope}");
        cache[scope] = rootControl;
        return rootControl;
    }

    private void ExecuteReport(Entity entity)
    {
        var sb = new StringBuilder();
        int formCount = 0;
        int totalControls = 0;

        // P7：容器归属统计——空容器显式暴露（「控件对象存在 ≠ 容器内可见」的容器级判据）
        var containerStats = new List<(string Name, string Type, int DirectChildren)>();

        ControlFinder.ForEachOpenFormSafe(form =>
        {
            var formControls = GetAllControlsFrom(form);
            totalControls += formControls.Count;

            sb.AppendLine();
            sb.AppendLine($"[表单 {formCount}] {form.Text} ({form.Name})");
            sb.AppendLine($"  位置: ({form.Left}, {form.Top}) 大小: ({form.Width}, {form.Height})");
            sb.AppendLine($"  控件数: {formControls.Count}");

            foreach (var ctrl in formControls)
            {
                sb.AppendLine($"    {ctrl.GetType().Name} '{ctrl.Name}': pos=({ctrl.Location.X},{ctrl.Location.Y}) size=({ctrl.Width},{ctrl.Height}) text='{TruncateText(ctrl.Text, 30)}'");

                // P7：收集命名容器（含空容器——Panel/GroupBox/SplitContainer 等即使无子控件也要暴露）
                if (ctrl.Name.Length > 0 && IsContainerControl(ctrl))
                {
                    containerStats.Add((ctrl.Name, ctrl.GetType().Name, ctrl.Controls.Count));
                }
            }

            formCount++;
            return false;
        });

        // P7：容器归属统计（直接子控件数）——sideContainer 等空容器在此显式暴露，不再淹没在控件总数里
        sb.AppendLine();
        sb.AppendLine("--- 容器归属统计（直接子控件数）---");
        if (containerStats.Count == 0)
        {
            sb.AppendLine("  （未发现命名容器）");
        }
        else
        {
            foreach (var (name, type, count) in containerStats.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase))
            {
                sb.AppendLine($"  [{type}] {name}: {count} 个直接子控件{(count == 0 ? "  ← 空容器（未挂载任何子控件）" : "")}");
            }
        }

        // S4：容器注册状态（MountState）——读 ContainerRegistry 状态为主；树遍历归属仅作诊断对照。
        // 树遍历无法区分「空容器（0 子）未挂载」与「已注册未渲染」，故补注册视角并双向校验告警。
        sb.AppendLine();
        sb.AppendLine("--- 容器注册状态（MountState；树遍历口径仅作诊断对照）---");
        var registry = AppServiceProvider.Resolve<ContainerRegistry>();
        var registryContainers = registry?.GetAll() ?? new List<FrameworkAbstractions.ContainerAbstractions.ContainerHandle>();
        if (registryContainers.Count == 0)
        {
            sb.AppendLine("  （无已登记容器）");
        }
        else
        {
            foreach (var handle in registryContainers.OrderBy(h => h.Id.HostKey, StringComparer.OrdinalIgnoreCase))
            {
                var hostKey = handle.Id.HostKey;
                var treeCount = containerStats
                    .FirstOrDefault(s => string.Equals(s.Name, hostKey, StringComparison.OrdinalIgnoreCase)).DirectChildren;
                var ownedCount = handle.Control?.Controls.Count ?? -1;
                var mismatch = ownedCount >= 0 && treeCount >= 0 && ownedCount != treeCount;
                sb.AppendLine($"  [{handle.Id.Role}] {hostKey}: scope={handle.Id.ScopeId} state={handle.State}" +
                    $" 子控件(端口口径)={ownedCount}" +
                    (treeCount >= 0 ? $" 树遍历={treeCount}{(mismatch ? "  ⓘ 树遍历口径不一致（诊断对照）" : "")}" : ""));
            }
        }

        sb.Insert(0, $"--- 布局报告 ---\n表单数量: {formCount}\n");
        sb.AppendLine();
        sb.AppendLine($"--- 总计: {formCount} 个表单, {totalControls} 个控件 ---");

        var result = sb.ToString();
        FrontEndDrivenLog.Log("Layout_Report", new { formCount, controlCount = totalControls, containerCount = containerStats.Count });
        SetSuccess(entity, result);
    }

    /// <summary>P7：容器控件判定（Panel/GroupBox/SplitContainer/ContainerControl/TabControl 或已有子控件）。</summary>
    private static bool IsContainerControl(Control ctrl)
        => ctrl is Panel
        || ctrl is GroupBox
        || ctrl is SplitContainer
        || ctrl is System.Windows.Forms.ContainerControl
        || ctrl is TabControl
        || ctrl.Controls.Count > 0;

    private void ExecuteJsonReport(Entity entity)
    {
        var entries = new List<object>();
        int totalControls = 0;

        ControlFinder.ForEachOpenFormSafe(form =>
        {
            var controls = GetAllControlsFrom(form);
            totalControls += controls.Count;

            var formData = new
            {
                formIndex = entries.Count,
                formName = form.Name,
                formText = form.Text,
                bounds = new { left = form.Left, top = form.Top, width = form.Width, height = form.Height },
                controls = controls.Select(ctrl => new
                {
                    name = ctrl.Name,
                    type = ctrl.GetType().Name,
                    text = ctrl.Text,
                    bounds = new { left = ctrl.Location.X, top = ctrl.Location.Y, width = ctrl.Width, height = ctrl.Height },
                    visible = ctrl.Visible,
                    enabled = ctrl.Enabled
                }).ToList()
            };
            entries.Add(formData);
            return false;
        });

        var json = System.Text.Json.JsonSerializer.Serialize(new { forms = entries, totalControls });
        FrontEndDrivenLog.Log("Layout_JsonReport", new { json });
        SetSuccess(entity, json);
    }

    private void ExecuteOverlaps(Entity entity)
    {
        var walker = new ControlTreeWalker();
        var calculator = new OverlapCalculator(BuildEntityTreeParentResolver);
        var allMetadata = new List<ControlMetadata>();

        ControlFinder.ForEachOpenFormSafe(form => { allMetadata.AddRange(walker.Walk(form)); return false; });

        if (allMetadata.Count < 2)
        {
            FrontEndDrivenLog.Log("Layout_Overlaps", new
            {
                totalControls = allMetadata.Count,
                totalPairs = 0,
                problematicCount = 0,
                hasOverlaps = false
            });
            SetSuccess(entity, "layout overlaps: 控件数不足（<2），跳过重叠检测");
            return;
        }

        var result = calculator.Calculate(allMetadata);
        FrontEndDrivenLog.Log("Layout_Overlaps", new
        {
            totalControls = result.TotalControls,
            totalPairs = result.AllPairs.Count,
            problematicCount = result.ProblematicOverlapCount,
            hasOverlaps = result.HasProblematicOverlaps
        });

        if (!result.HasProblematicOverlaps)
        {
            SetSuccess(entity, $"layout overlaps: 未检测到非父子关系的控件重叠（共 {result.TotalControls} 个控件）");
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine($"检测到 {result.ProblematicOverlapCount} 处非父子关系控件重叠（共 {result.TotalOverlapCount} 个总重叠对，{result.TotalControls} 个控件）:");

        foreach (var pair in result.ProblematicPairs)
        {
            var ab = pair.ControlABounds;
            var bb = pair.ControlBBounds;
            var parentInfo = !string.IsNullOrEmpty(pair.ControlAParentName)
                ? $" 父容器='{pair.ControlAParentName}'"
                : "";
            sb.AppendLine($"  '{pair.ControlAName}({pair.ControlAType})' ↔ '{pair.ControlBName}({pair.ControlBType})'{parentInfo} 在区域 ({ab.X},{ab.Y},{ab.Width},{ab.Height})∩({bb.X},{bb.Y},{bb.Width},{bb.Height}) 重叠率={pair.OverlapPercentage:P1}");
        }

        var output = sb.ToString();
        FrontEndDrivenLog.Log("Layout_Overlaps_ProblemReport", new { report = output });
        SetSuccess(entity, output);
    }


    private static List<Control> GetAllControls()
    {
        var all = new List<Control>();
        ControlFinder.ForEachOpenFormSafe(form =>
        {
            all.AddRange(GetAllControlsFrom(form));
            return false;
        });
        return all;
    }

    private static List<Control> GetAllControlsFrom(Control parent)
    {
        var result = new List<Control>();
        if (parent == null) return result;

        foreach (Control ctrl in parent.Controls)
        {
            result.Add(ctrl);
            if (ctrl.Controls.Count > 0)
            {
                result.AddRange(GetAllControlsFrom(ctrl));
            }
        }
        return result;
    }

    private ControlMetadata? BuildEntityTreeParentResolver(ControlMetadata control)
    {
        if (control == null || string.IsNullOrEmpty(control.Name))
            return null;

        Entity? currentEntity = FindEntityByName(control.Name);
        if (currentEntity == null || !currentEntity.Value.IsValid)
            return null;

        var entity = currentEntity.Value;
        var parent = ResolveParent(entity);
        return parent != null ? BuildMetadataFromEntity(parent.Value) : null;
    }

    private Entity? FindEntityByName(string name)
    {
        // D1 收敛：按名查实体统一经 EntityQueryService.FindByName（含 scope 过滤），删除手写遍历副本
        return AppServiceProvider.Resolve<EntityQueryService>()?.FindByName(name);
    }

    private Entity? ResolveParent(Entity entity)
    {
        var queryService = AppServiceProvider.Resolve<EntityQueryService>();
        var world = queryService?.EntityManager;
        if (world == null) return null;

        var current = entity;
        while (current.IsValid
               && world.TryGetComponent<ParentEntityComponent>(current, out var parentComp)
               && parentComp.ParentEntity.IsValid)
        {
            var parent = parentComp.ParentEntity;

            if (world.HasComponent<SplitterPanelComponent>(parent))
            {
                current = parent;
                continue;
            }

            return parent;
        }
        return null;
    }

    private ControlMetadata? BuildMetadataFromEntity(Entity entity)
    {
        if (!entity.IsValid) return null;

        var queryService = AppServiceProvider.Resolve<EntityQueryService>();
        var world = queryService?.EntityManager;
        if (world == null) return null;

        string? name = null;
        string? typeName = null;
        if (world.TryGetComponent<ControlInfoComponent>(entity, out var info))
            name = info.Name;
        if (world.TryGetComponent<ControlTypeComponent>(entity, out var typeComp))
            typeName = typeComp.Type;

        if (string.IsNullOrEmpty(name))
            return null;

        return new ControlMetadata
        {
            Name = name,
            TypeName = typeName ?? string.Empty,
            ParentName = string.Empty,
            Width = 0,
            Height = 0,
            ScreenLeft = 0,
            ScreenTop = 0
        };
    }

    private static string TruncateText(string text, int maxLen)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLen)
            return text ?? "";
        return text[..maxLen] + "...";
    }
}