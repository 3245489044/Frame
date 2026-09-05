
#nullable enable

using Modules.ReactiveData.Abstractions;

namespace Modules.ReactiveData
{
    public static class UsageExamples
    {
        public static void BasicDataStoreExample()
        {
            using var store = new ReactiveDataStore();

            var subscription = store.DataChanged.Subscribe(e =>
            {
                Console.WriteLine($"[{e.ChangeType}] {e.Key}: {e.OldValue} -> {e.NewValue}");
            });

            var keySub = store.SubscribeToKey("user.name", e =>
            {
                Console.WriteLine($"用户名变更: {e.OldValue} -> {e.NewValue}");
            });

            var typeSub = store.SubscribeToType<int>(e =>
            {
                Console.WriteLine($"整数数据变更: {e.OldValue} -> {e.NewValue}");
            });

            store.Set("user.name", "Alice");
            store.Set("user.age", 30);
            store.Set("user.name", "Bob");
            store.Remove("user.age");

            subscription.Dispose();
            keySub.Dispose();
            typeSub.Dispose();
        }

        public static void ReactorPipelineExample()
        {
            using var store = new ReactiveDataStore();
            var pipeline = new ReactorPipeline();

            pipeline.Register(new UserNameReactor());
            pipeline.Register(new AgeReactor());

            using var connection = pipeline.ConnectTo(store);

            store.Set("user.name", "Alice");
            store.Set("user.age", 25);
        }

        public static void LayoutReactivityExample()
        {
            using var layoutStore = new ReactiveDataStore();

            layoutStore.SubscribeToKey("layout.width", e =>
            {
                Console.WriteLine($"布局宽度变更: {e.OldValue} -> {e.NewValue}");
            });

            layoutStore.SubscribeToKey("layout.height", e =>
            {
                Console.WriteLine($"布局高度变更: {e.OldValue} -> {e.NewValue}");
            });

            layoutStore.Set("layout.width", 1024);
            layoutStore.Set("layout.height", 768);
        }

        public static void ConfigChangeExample()
        {
            using var configStore = new ReactiveDataStore();

            configStore.SubscribeToKey("config.theme", e =>
            {
                Console.WriteLine($"主题变更: {e.OldValue} -> {e.NewValue}");
            });

            configStore.SubscribeToKey("config.language", e =>
            {
                Console.WriteLine($"语言变更: {e.OldValue} -> {e.NewValue}");
            });

            configStore.Set("config.theme", "dark");
            configStore.Set("config.language", "zh-CN");
        }

        public static void ThrottledExample()
        {
            using var store = new ReactiveDataStore();

            var throttled = store.Throttle(TimeSpan.FromMilliseconds(300));
            using var sub = throttled.Subscribe(e =>
            {
                Console.WriteLine($"节流后变更: {e.Key} = {e.NewValue}");
            });

            for (int i = 0; i < 100; i++)
            {
                store.Set("counter", i);
            }
        }
    }


    public class UserNameReactor : IReactor<string>
    {
        public float Priority => 100;

        public bool ShouldReact(string data)
        {
            return data.Length > 0;
        }

        public void React(string data)
        {
            Console.WriteLine($"[UserNameReactor] 用户名已更新: {data}");
        }
    }

    public class AgeReactor : IReactor<int>
    {
        public float Priority => 50;

        public bool ShouldReact(int data)
        {
            return data >= 0 && data <= 150;
        }

        public void React(int data)
        {
            Console.WriteLine($"[AgeReactor] 年龄已更新: {data}");
        }
    }
}
