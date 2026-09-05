
#nullable enable

using ECS.Core;
using Modules.ComponentData.ContextDriven;
using Modules.ComponentData.Core;
using Modules.ComponentData.Reactor;

namespace Modules.ComponentData
{
    public static class UsageExamples
    {
        public static void BasicStoreExample()
        {
            using var store = new ComponentDataStore();

            var subscription = store.DataChanged.Subscribe(args =>
            {
                Console.WriteLine($"[{args.ChangeType}] Key={args.Key}, NewValue={args.NewValue}");
            });

            var keySub = store.ObserveKey("player.health").Subscribe(args =>
            {
                Console.WriteLine($"Player health changed: {args.OldValue} -> {args.NewValue}");
            });

            store.Set("player.health", 100);
            store.Set("player.score", 0);

            store.Set("player.health", 80);

            store.Remove("player.score");

            store.Clear();

            subscription.Dispose();
            keySub.Dispose();
        }

    }



    public class HealthReactor : IEcsComponentReactor<int>
    {
        public float Priority => 50;

        public bool ShouldReact(int health) => health < 30;

        public void React(int health, EntityManager entityManager)
        {
            Console.WriteLine($"[HealthReactor] Low health warning: {health}!");
        }
    }

    public class ScoreReactor : IEcsComponentReactor<int>
    {
        public float Priority => 30;

        public bool ShouldReact(int score) => true;

        public void React(int score, EntityManager entityManager)
        {
            Console.WriteLine($"[ScoreReactor] Score updated: {score}");
        }
    }

    public class HighPriorityReactor : IEcsComponentReactor<string>
    {
        public float Priority => 100;
        public bool ShouldReact(string data) => true;
        public void React(string data, EntityManager entityManager)
        {
            Console.WriteLine($"[HighPriority] Reacting to: {data}");
        }
    }

    public class MediumPriorityReactor : IEcsComponentReactor<string>
    {
        public float Priority => 50;
        public bool ShouldReact(string data) => true;
        public void React(string data, EntityManager entityManager)
        {
            Console.WriteLine($"[MediumPriority] Reacting to: {data}");
        }
    }

    public class LowPriorityReactor : IEcsComponentReactor<string>
    {
        public float Priority => 10;
        public bool ShouldReact(string data) => true;
        public void React(string data, EntityManager entityManager)
        {
            Console.WriteLine($"[LowPriority] Reacting to: {data}");
        }
    }
}
