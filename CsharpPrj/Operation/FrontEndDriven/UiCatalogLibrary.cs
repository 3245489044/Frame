#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommandFramework.Abstractions;
using ECS.Bridge;
using ECS.Core;
using FrameworkAbstractions.ComponentRegistry;
using FrontEndDriven.ControlLookup;
using FrontEndDriven.Shared.Catalog;
using Modules.Registry;
using TConfig;
using UiPathManager = FrontEndDriven.Shared.Utils.PathManager;

namespace FrontEndDriven;

/// <summary>通用文件目录函数库：把「列出目录 / 按 id 加载文件 / 加载说明」暴露为 JSON 驱动 UI 可绑定的通用函数。
/// 目录发现/扫描为框架能力（FileCatalogService + PathManager），默认源目录为 Agent 模板库根（PathManager.GetAgentRunTemplatesRoot），
/// 不绑定任何领域目录。零参数/单参设计以规避「无参数来源→跳过执行」的绑定语义。</summary>
[FunctionLibrary]
public static class UiCatalogLibrary
{
    public static void RegisterAll(ITrConfigRegistry registry)
    {
        registry.Register("Ui_ListCatalog",
            (Func<ListData>)(() => ListCatalog(null)),
            "列出模板目录文件（目录发现，框架模板根，按文件夹分类；含全部文件类型）");

        registry.Register("Ui_ListCatalogTree",
            (Func<string>)(() => ListCatalogTree(null)),
            "列出模板目录树（层级：文件夹=分类，文件=条目含扩展名；文本=每行 folder 或 folder/file，供 TreeView 重建节点）");

        registry.Register("Ui_InspectTemplateTree",
            (Func<string>)(() => InspectTemplateTree()),
            "诊断：读取物理 templateListView（TreeView）节点数/可见性，区分『树没建』与『建了没画』");

        registry.Register("Ui_LoadCatalog",
            (Func<string, Task<string>>)(id => LoadCatalog(null, id)),
            new IParamConfig[] { new ParameterDescriptor { Name = "id", Type = typeof(string) } },
            "按相对 id 加载模板目录文件全文（任意类型；id=folder/文件名.扩展名）");

        registry.Register("Ui_LoadCatalogDoc",
            (Func<string, Task<string>>)(id => LoadCatalogDoc(null, id)),
            new IParamConfig[] { new ParameterDescriptor { Name = "id", Type = typeof(string) } },
            "按相对 id 加载模板说明（缺省回退分类 README）");
    }

    /// <summary>默认扫描根为 Agent 请求体模板库（Templates/AgentRun），id = 分类/文件名；调用方可传入 Dir 覆盖。</summary>
    private static FileCatalogSpec Spec(string? dir)
        => new FileCatalogSpec { Dir = string.IsNullOrWhiteSpace(dir) ? UiPathManager.GetAgentRunTemplatesRoot() : dir };

    private static ListData ListCatalog(string? dir)
    {
        var entries = FileCatalogService.Scan(Spec(dir));
        var ordered = FileCatalogService.OrderEntries(entries);
        return ListData.From(ordered.Select(FileCatalogService.FormatItem));
    }

    /// <summary>目录树文本：每个文件夹先输出一行 folder，再输出该文件夹下每文件一行 folder/file（供 TreeView 先文件夹后文件）。</summary>
    private static string ListCatalogTree(string? dir)
    {
        var entries = FileCatalogService.Scan(Spec(dir));
        var ordered = FileCatalogService.OrderEntries(entries);
        var lines = new List<string>();
        string? lastFolder = null;
        foreach (var e in ordered)
        {
            if (!string.Equals(e.Category, lastFolder, StringComparison.OrdinalIgnoreCase))
            {
                lastFolder = e.Category;
                if (!string.IsNullOrEmpty(e.Category))
                    lines.Add(e.Category);
            }
            lines.Add(e.Id);
        }
        return string.Join("\n", lines);
    }

    /// <summary>诊断：读取物理 templateListView（TreeView）真实状态——区分『树没建』(节点=0) 与『建了没画』(节点&gt;0 但控件不可见/未挂句柄)。</summary>
    private static string InspectTemplateTree()
    {
        var query = AppServiceProvider.Resolve<EntityQueryService>();
        var pool = AppServiceProvider.Resolve<ControlPool>();
        if (query == null || pool == null)
            return $"查询/控件池未注册 (query={query != null}, pool={pool != null})";

        var entity = EntityLookup.FindByName(query, "templateListView");
        if (!entity.IsValid)
            return "未找到 templateListView 实体";

        var ctrl = pool.GetControl(entity);
        if (ctrl == null)
            return "templateListView 无物理控件（实体存在但未挂到 ControlPool）";

        if (ctrl is not System.Windows.Forms.TreeView tv)
            return $"templateListView 物理控件类型={ctrl.GetType().Name}（非 TreeView）";

        var total = CountNodes(tv.Nodes);
        return $"TreeView: 顶层文件夹={tv.Nodes.Count}, 节点总数={total}, IsHandleCreated={tv.IsHandleCreated}, Visible={tv.Visible}, IsDisposed={tv.IsDisposed}, Size={tv.Width}x{tv.Height}";
    }

    private static int CountNodes(System.Windows.Forms.TreeNodeCollection nodes)
    {
        var count = 0;
        foreach (System.Windows.Forms.TreeNode node in nodes)
        {
            count += 1 + CountNodes(node.Nodes);
        }
        return count;
    }

    private static Task<string> LoadCatalog(string? dir, string id)
        => Task.FromResult(LoadWithDiagnostic(Spec(dir), id, "文件", FileCatalogService.LoadText));

    private static Task<string> LoadCatalogDoc(string? dir, string id)
        => Task.FromResult(LoadWithDiagnostic(Spec(dir), id, "模板说明", FileCatalogService.LoadDocText));

    /// <summary>
    /// 通用「按 id 加载并诊断兜底」实现（近复制收敛）：ExtractId → 空 id 提示 AvailableIds → 加载失败兜底文案。
    /// <paramref name="loader"/> 区分文件全文加载 / 说明文档加载；<paramref name="kind"/> 用于兜底文案（"文件"/"模板说明"）。
    /// </summary>
    private static string LoadWithDiagnostic(FileCatalogSpec spec, string id, string kind, Func<FileCatalogSpec, string, string?> loader)
    {
        var clean = FileCatalogService.ExtractId(id);
        if (clean.Length == 0)
            return "（未指定 id）可用: " + FileCatalogService.AvailableIds(spec);
        try
        {
            var text = loader(spec, clean);
            if (text is null)
                return $"（未找到{kind}）'{clean}'，可用: " + FileCatalogService.AvailableIds(spec);
            return text;
        }
        catch (Exception ex)
        {
            return $"（读取{kind}失败）'{clean}' :: {ex.Message}";
        }
    }
}