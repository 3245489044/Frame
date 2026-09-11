namespace DataCenter.Hosting;

/// <summary>
/// 换代计划（<see cref="DataCenterHost.SwapAsync"/> 的输入）—— 显式声明本次换代的内容与边界钩子。
/// 纪律：代数递增由宿主单点完成（generation+1），指纹/结构版本由计划提供；
/// Drain/Resume 为宿主侧的写入边界钩子（A0 = 同 ALC 内代际推进；A5 = 接入新 ALC 装配）。
/// </summary>
/// <param name="Fingerprint">内容指纹（换代去重与在途版本判据；null = 保持未计算）。</param>
/// <param name="SchemaVersion">运行时数据结构版本（null = 保持不变；非 null = 迁移触发）。</param>
/// <param name="Drain">停写钩子（暂停新写、在途推至边界）；null = 跳过。</param>
/// <param name="Resume">恢复钩子（新代数就绪后放行写入）；null = 跳过。</param>
public sealed record GenerationPlan(
    string? Fingerprint = null,
    int? SchemaVersion = null,
    Func<CancellationToken, Task>? Drain = null,
    Func<CancellationToken, Task>? Resume = null);
