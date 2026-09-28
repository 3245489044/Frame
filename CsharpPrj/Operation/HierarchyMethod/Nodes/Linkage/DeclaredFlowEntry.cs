using System;

namespace Operation.HierarchyMethod.Nodes.Linkage;

/// <summary>
/// 簿记流调用描述（**数据行**）—— 新增一条簿记流的唯一新增成本 = 数据表加一行 + 1 个 .ushar + 1 个 fn 库；
/// 驱动 / 转发 / 未装配诊断由 <see cref="DeclaredFlowDelegateBase"/> 与声明流执行器承担（DUP-7 收口）。
/// </summary>
/// <param name="FileName">声明面文件名（Config/workflows/ 下裸名；须已在 DeclaredFlowIds 登记）。</param>
/// <param name="FlowName">[Flow] 流号（诊断 / 对账用）。</param>
/// <param name="Label">日志标签（人读归因）。</param>
/// <param name="SubjectKey">主语入参键名（= 声明面 inports= 首项；同流多主语时取主用项）。</param>
public sealed record DeclaredFlowEntry(string FileName, string FlowName, string Label, string SubjectKey);
