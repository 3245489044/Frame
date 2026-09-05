
using System.Reflection;

namespace Modules.ComponentData.Reflection
{
    public interface IMemberResolver
    {
        MethodInfo ResolveMethod(InvocationDescriptor desc);
        PropertyInfo ResolveProperty(InvocationDescriptor desc);
        FieldInfo ResolveField(InvocationDescriptor desc);
        ConstructorInfo ResolveConstructor(InvocationDescriptor desc);
    }
}
