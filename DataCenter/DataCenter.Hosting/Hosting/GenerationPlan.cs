namespace DataCenter.Hosting;

/// <summary>
/// 换代计划（<see cref="DataCenterHost.SwapAsync"/> 的输入）—— 显式声明本次换代的内容与边界钩子。
/// 纪律：代数递增由宿主单点完成（generation+1），指纹/结构版本由计划提供；
/// Drain/Resume 为宿主侧的写入边界钩子，Export/Import 为跨代数载荷钩子
/// （A0 = 同 ALC 内代际推进；A5/S4-2 = 接入新 ALC 装配入口）。
/// </summary>
/// <param name="Fingerprint">内容指纹（换代去重与在途版本判据；null = 沿用既有指纹，**不置空**；B2/F-18）。</param>
/// <param name="SchemaVersion">运行时数据结构版本（null = 保持不变；非 null = 迁移触发）。</param>
/// <param name="Drain">停写钩子（暂停新写、在途推至边界）；null = 跳过。</param>
/// <param name="Resume">放行写入钩子（在 <c>finally</c> 中调用且不接收取消令牌 —— 换代成功/失败/取消都必须放行写入）；null = 跳过。</param>
/// <param name="Export">导出钩子（旧代数释放前调用；返回跨代迁移载荷，null 返回值按空载荷处理）；null = 跳过。</param>
/// <param name="Import">导入钩子（新代数绑根后调用；接收 Export 的载荷，未 Export 时为空载荷）；null = 跳过。</param>
public sealed record GenerationPlan(
    string? Fingerprint = null,
    int? SchemaVersion = null,
    Func<CancellationToken, Task>? Drain = null,
    Func<CancellationToken, Task>? Resume = null,
    Func<CancellationToken, Task<GenerationPayload?>>? Export = null,
    Func<GenerationPayload, CancellationToken, Task>? Import = null);
