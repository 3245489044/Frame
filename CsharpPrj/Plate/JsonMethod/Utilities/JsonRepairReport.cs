namespace Plate.JsonMethod.Utilities
{
    /// <summary>
    /// 宽松修复命中明细（<see cref="Changed"/> = false 表示原文未被改动）。
    /// 权威产生方：<see cref="JsonEscapeRepair"/>；消费方须据此**可见化**（修复不得静默）。
    /// </summary>
    /// <param name="EscapeHits">字符串字面量内被加倍的反斜杠处数。</param>
    /// <param name="TrimmedChars">根值闭合后被截断的尾随噪声字符数。</param>
    public readonly record struct JsonRepairReport(int EscapeHits, int TrimmedChars)
    {
        /// <summary>是否发生了任何改动。</summary>
        public bool Changed => EscapeHits > 0 || TrimmedChars > 0;
    }
}
