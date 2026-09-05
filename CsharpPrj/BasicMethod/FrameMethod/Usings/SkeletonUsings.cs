namespace SkeletonFlow.App
{
    #region 核心流式构建器

    public class Flow<TInput, TOutput>
    {
        private readonly ISkeleton<TInput, TOutput> _skeleton;

        internal Flow(ISkeleton<TInput, TOutput> skeleton)
        {
            _skeleton = skeleton ?? throw new ArgumentNullException(nameof(skeleton));
        }

        public ISkeleton<TInput, TOutput> Skeleton => _skeleton;

        public Flow<TInput, TNewOutput> Then<TNewOutput>(ISkeleton<TOutput, TNewOutput> next)
            => new Flow<TInput, TNewOutput>(_skeleton.Then(next));

        public TOutput Execute(TInput input, SkeletonContext context = null)
            => _skeleton.Execute(input, context);

        public Task<TOutput> ExecuteAsync(TInput input, SkeletonContext context = null)
            => _skeleton.ExecuteAsync(input, context);
    }

    public static class Flow
    {
        private static readonly Lazy<DefaultSkeletonRegistry> _defaultRegistry = new(() =>
        {
            var reg = new DefaultSkeletonRegistry();
            return reg;
        });

        public static ISkeletonRegistry DefaultRegistry { get; set; } = _defaultRegistry.Value;

        public static Flow<T, T> Start<T>() => new Flow<T, T>(new IdentitySkeleton<T>());

        public static Flow<TInput, TOutput> FromSkeleton<TInput, TOutput>(ISkeleton<TInput, TOutput> skeleton)
            => new Flow<TInput, TOutput>(skeleton);

        public static Flow<TInput, TOutput> Load<TInput, TOutput>(string configText, IServiceProvider services = null)
        {
            var registry = DefaultRegistry;
            var parser = new JsonConfigParser();
            var config = parser.Parse(configText);
            var skeleton = registry.Resolve(config, services);
            if (skeleton is ISkeleton<TInput, TOutput> typed)
                return new Flow<TInput, TOutput>(typed);
            throw new InvalidOperationException($"解析出的骨架类型与期望的 {typeof(TInput).Name}->{typeof(TOutput).Name} 不匹配。");
        }

        public static Flow<TInput, (TOutput1, TOutput2)> Parallel<TInput, TOutput1, TOutput2>(
            Flow<TInput, TOutput1> flow1,
            Flow<TInput, TOutput2> flow2)
            => new Flow<TInput, (TOutput1, TOutput2)>(flow1.Skeleton.Parallel(flow2.Skeleton));
    }

    internal class IdentitySkeleton<T> : SkeletonBase<T, T>
    {
        public IdentitySkeleton() : base("Identity", "Pass through input") { }
        protected override T ExecuteCore(T input, SkeletonContext context) => input;
        protected override Task<T> ExecuteCoreAsync(T input, SkeletonContext context) => Task.FromResult(input);
    }

    #endregion

    #region 针对集合的扩展方法

    public static class FlowEnumerableExtensions
    {
        public static Flow<TInput, IEnumerable<TElement>> Filter<TInput, TElement>(
            this Flow<TInput, IEnumerable<TElement>> flow,
            Func<TElement, bool> predicate,
            string name = null)
            => flow.Then(BuiltInSkeletons.Filter(name ?? "Filter", predicate));

        public static Flow<TInput, IEnumerable<TResult>> Map<TInput, TElement, TResult>(
            this Flow<TInput, IEnumerable<TElement>> flow,
            Func<TElement, TResult> selector,
            string name = null)
            => flow.Then(BuiltInSkeletons.Map(name ?? "Map", selector));

        public static Flow<TInput, TAccumulate> Reduce<TInput, TElement, TAccumulate>(
            this Flow<TInput, IEnumerable<TElement>> flow,
            TAccumulate seed,
            Func<TAccumulate, TElement, TAccumulate> func,
            string name = null)
            => flow.Then(BuiltInSkeletons.Reduce(name ?? "Reduce", seed, func));

        public static Flow<TInput, IEnumerable<TElement>> Skip<TInput, TElement>(
            this Flow<TInput, IEnumerable<TElement>> flow,
            int count,
            string name = null)
            => flow.Then(BuiltInSkeletons.Skip<TElement>(name ?? "Skip", count));

        public static Flow<TInput, IEnumerable<TElement>> Take<TInput, TElement>(
            this Flow<TInput, IEnumerable<TElement>> flow,
            int count,
            string name = null)
            => flow.Then(BuiltInSkeletons.Take<TElement>(name ?? "Take", count));

        public static Flow<TInput, IEnumerable<TElement>> Distinct<TInput, TElement>(
            this Flow<TInput, IEnumerable<TElement>> flow,
            IEqualityComparer<TElement> comparer = null,
            string name = null)
            => flow.Then(BuiltInSkeletons.Distinct(name ?? "Distinct", comparer));

        public static Flow<TInput, IEnumerable<IGrouping<TKey, TElement>>> GroupBy<TInput, TElement, TKey>(
            this Flow<TInput, IEnumerable<TElement>> flow,
            Func<TElement, TKey> keySelector,
            IEqualityComparer<TKey> comparer = null,
            string name = null)
            => flow.Then(BuiltInSkeletons.GroupBy(name ?? "GroupBy", keySelector, comparer));

        public static Flow<TInput, IOrderedEnumerable<TElement>> OrderBy<TInput, TElement, TKey>(
            this Flow<TInput, IEnumerable<TElement>> flow,
            Func<TElement, TKey> keySelector,
            bool descending = false,
            IComparer<TKey> comparer = null,
            string name = null)
            => flow.Then(BuiltInSkeletons.OrderBy(name ?? "OrderBy", keySelector, descending, comparer));

        public static Flow<TInput, TElement> First<TInput, TElement>(
            this Flow<TInput, IEnumerable<TElement>> flow,
            Func<TElement, bool> predicate = null,
            string name = null)
            => flow.Then(BuiltInSkeletons.First<TElement>(name ?? "First", predicate));

        public static Flow<TInput, TElement> Single<TInput, TElement>(
            this Flow<TInput, IEnumerable<TElement>> flow,
            Func<TElement, bool> predicate = null,
            string name = null)
            => flow.Then(BuiltInSkeletons.Single<TElement>(name ?? "Single", predicate));

        public static Flow<TInput, List<TElement>> ToList<TInput, TElement>(
            this Flow<TInput, IEnumerable<TElement>> flow,
            string name = null)
            => flow.Then(BuiltInSkeletons.ToList<TElement>(name ?? "ToList"));

        public static Flow<TInput, TElement[]> ToArray<TInput, TElement>(
            this Flow<TInput, IEnumerable<TElement>> flow,
            string name = null)
            => flow.Then(BuiltInSkeletons.ToArray<TElement>(name ?? "ToArray"));

        public static Flow<TInput, IEnumerable<TElement>> ParallelForEach<TInput, TElement>(
            this Flow<TInput, IEnumerable<TElement>> flow,
            Func<TElement, TElement> transform,
            int dop = -1,
            string name = null)
            => flow.Then(BuiltInSkeletons.ParallelForEach(name ?? "ParallelForEach", transform, dop));
    }

    #endregion

    #region 控制流扩展（分支、循环、并行处理）

    public static class FlowControlExtensions
    {
        public static Flow<TInput, TOutput> Branch<TInput, TOutput>(
            this Flow<TInput, TOutput> flow,
            Func<TOutput, bool> condition,
            Flow<TOutput, TOutput> trueBranch,
            Flow<TOutput, TOutput> falseBranch)
        {
            var condSkel = new DelegateSkeleton<TOutput, bool>("BranchCondition",
                (i, _) => condition(i),
                (i, _) => Task.FromResult(condition(i)));

            var trueSkel = trueBranch.Skeleton;
            var falseSkel = falseBranch.Skeleton;

            var branchSkel = condSkel.Branch(trueSkel, falseSkel);

            return flow.Then<TOutput>(branchSkel);
        }

        public static Flow<TInput, TOutput> Loop<TInput, TOutput>(
            this Flow<TInput, TOutput> flow,
            Func<TOutput, bool> condition,
            Flow<TOutput, TOutput> body)
        {
            var condSkel = new DelegateSkeleton<TOutput, bool>("LoopCondition",
                (i, _) => condition(i),
                (i, _) => Task.FromResult(condition(i)));

            var bodySkel = body.Skeleton;

            var loopSkel = condSkel.Loop(bodySkel);

            return flow.Then<TOutput>(loopSkel);
        }

        public static Flow<IEnumerable<TInput>, IEnumerable<TOutput>> ForEach<TInput, TOutput>(
            this Flow<TInput, TOutput> elementFlow)
            => new Flow<IEnumerable<TInput>, IEnumerable<TOutput>>(elementFlow.Skeleton.ForEach());

        public static Flow<IEnumerable<TInput>, IEnumerable<TOutput>> ParallelForEach<TInput, TOutput>(
            this Flow<TInput, TOutput> elementFlow,
            int degreeOfParallelism = -1)
        {
            var parallelSkel = new ParallelForEachSkeleton<TInput, TOutput>(degreeOfParallelism);
            ((ICompositeSkeleton)parallelSkel).SetChildren(new[] { elementFlow.Skeleton });
            return new Flow<IEnumerable<TInput>, IEnumerable<TOutput>>(parallelSkel);
        }

        public static Flow<IEnumerable<TInput>, IEnumerable<TOutput>> SelectMany<TInput, TOutput>(
            this Flow<TInput, IEnumerable<TOutput>> elementFlow)
            => new Flow<IEnumerable<TInput>, IEnumerable<TOutput>>(elementFlow.Skeleton.SelectMany());
    }

    #endregion

    #region 复合骨架构建辅助（用于并行组合等）

    public static class CompositeFlow
    {
        public static Flow<TInput, TOutput> Sequential<TInput, TOutput>(
            IEnumerable<ISkeleton> children,
            string name = null,
            string description = null)
        {
            var seq = new SequentialSkeleton<TInput, TOutput>(name, description);
            seq.SetChildren(children);
            return new Flow<TInput, TOutput>(seq);
        }
    }

    #endregion
}

