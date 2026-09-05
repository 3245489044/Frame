namespace CSG.Unified;

public interface IOptimizationContext
{
    ITransformEngine? Engine { get; }

    IAdapterRegistry? AdapterRegistry { get; }
}
