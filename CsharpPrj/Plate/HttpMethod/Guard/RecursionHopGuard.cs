namespace Plate.HttpMethod.Guard
{
    /// <summary>
    /// 通用递归跳数护栏（泛化自 LLM 专用递归守卫，对齐"客户端→服务端→客户端"自环的纵深防御）。
    /// 递归会经同一端点自我回弹；超时挡不住"快循环"（每跳都很快、单请求不超时），故用跳数自增头硬限深度：
    /// <list type="bullet">
    ///   <item>服务端从入站请求头读跳数，超出 <see cref="MaxHop"/> 即 508 强制拒绝；</item>
    ///   <item>出站递归腿经调用方显式携带 <c>hop+1</c> 到出站头，使递归每一跳自增，深度确定性地被封顶。</item>
    /// </list>
    /// 请求头名与最大跳数均可参数化；默认值保持既有 wire 契约（X-Llm-Hop / 8），兼容既有调用方与 curl 冒烟契约。
    /// </summary>
    public sealed class RecursionHopGuard
    {
        /// <summary>默认最大允许递归跳数（服务端 re-entry 次数上限；超过即视为自环强制停止）。</summary>
        public const int DefaultMaxHop = 8;

        /// <summary>默认跳数请求头名（保持既有 wire 契约）。</summary>
        public const string DefaultHeaderName = "X-Llm-Hop";

        public RecursionHopGuard(int maxHop = DefaultMaxHop, string headerName = DefaultHeaderName)
        {
            MaxHop = maxHop;
            HeaderName = string.IsNullOrWhiteSpace(headerName) ? DefaultHeaderName : headerName.Trim();
        }

        /// <summary>最大允许递归跳数。</summary>
        public int MaxHop { get; }

        /// <summary>跳数请求头名。</summary>
        public string HeaderName { get; }

        /// <summary>跳数是否在允许范围内（0..MaxHop-1）。</summary>
        public bool IsWithinLimit(int hop) => hop >= 0 && hop < MaxHop;

        /// <summary>构造跳数请求头值。</summary>
        public string BuildHeaderValue(int hop) => hop.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}
