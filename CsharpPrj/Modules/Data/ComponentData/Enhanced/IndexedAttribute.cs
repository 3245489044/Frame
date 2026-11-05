namespace Modules.ComponentData.Enhanced
{
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public sealed class IndexedAttribute : Attribute { public bool Unique; }
}
