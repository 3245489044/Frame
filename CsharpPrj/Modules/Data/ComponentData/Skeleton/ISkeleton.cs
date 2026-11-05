
using System.Threading.Tasks;

namespace Modules.ComponentData.Skeleton
{
    public interface ISkeleton { }

    public interface ISkeleton<in TInput, TOutput> : ISkeleton
    {
        string Name { get; }
        string Description { get; }
        TOutput Execute(TInput input, SkeletonContext context = null);
        Task<TOutput> ExecuteAsync(TInput input, SkeletonContext context = null);
    }
}
