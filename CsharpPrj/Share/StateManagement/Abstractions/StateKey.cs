namespace Share.StateManagement
{
    /// <summary>
    /// 统一状态键：命名空间(域) + 作用域 + 键名。对齐 GlobalDecisionKey 轴绑定思想。
    /// 权威定义：[CR: axioms:AX-CORE.AxiomHierarchyStructure@see]（键结构即轴绑定）
    /// </summary>
    public readonly record struct StateKey(string Namespace, string Scope, string Key)
    {
        public string FullKey => $"{Namespace}:{Scope}:{Key}";

        public static StateKey Container(string scope) => new("container", scope, "state");
        public static StateKey HostBinding(string scope) => new("host_binding", scope, "hostKey");
        public static StateKey Composition() => new("composition", "*", "lifecycle");
        public static StateKey ScopeLifecycle(string scope) => new("scope", scope, "lifecycle");

        public override string ToString() => FullKey;
    }
}
