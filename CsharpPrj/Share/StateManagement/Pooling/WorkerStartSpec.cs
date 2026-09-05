namespace Share.StateManagement.Pooling
{
    /// <summary>
    /// worker 启动规格（框架为每个 worker 生成）。<see cref="Payload"/> 承载传输层特有配置
    /// （如 CLI 参数、环境变量、data-dir 等），框架不解释其内容。
    /// </summary>
    public sealed record WorkerStartSpec(string WorkerId, string? Payload = null);
}
