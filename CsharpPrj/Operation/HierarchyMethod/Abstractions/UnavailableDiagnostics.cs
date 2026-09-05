namespace Operation.HierarchyMethod.Abstractions
{
    /// <summary>
    /// 可空守卫诊断 —— 未装配端口 / 关系库时的返回占位常量（-1 / false / 空集），
    /// 使框架在未注入时返回诊断而不阻断 DB-only / 确定性路径（P5：不消耗 AI 额度）。
    /// </summary>
    public static class UnavailableDiagnostics
    {
        /// <summary>未装配时的等级占位（无有效 level 语义）。</summary>
        public const int UnavailableLevel = -1;

        /// <summary>未装配时的布尔占位。</summary>
        public const bool UnavailableFlag = false;

        /// <summary>未装配时的键占位。</summary>
        public const string UnavailableKey = "";

        /// <summary>未装配时的空键集（只增容器视图复用）。</summary>
        public static readonly IReadOnlyList<string> EmptyKeys = Array.Empty<string>();
    }
}
