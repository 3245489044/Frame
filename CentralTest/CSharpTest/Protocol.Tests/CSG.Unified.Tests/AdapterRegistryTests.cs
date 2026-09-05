using CSG.Unified;
using FluentAssertions;
using Xunit;

namespace CSG.Unified.Tests;

/// <summary>
/// 对齐收敛后的单模块 <see cref="AdapterRegistry"/> API（A 修复：旧多项目 RegisterConfigAdapter/AdaptToConfig/HasConfigAdapter
/// 等专用方法已收敛为泛型 Register&lt;TSource,TTarget&gt;/Adapt&lt;TTarget&gt;/HasAdapter&lt;TSource,TTarget&gt;/GetCapabilities&lt;TSource,TTarget&gt;）。
/// </summary>
public class AdapterRegistryTests
{
    private readonly AdapterRegistry _registry = new();


    [Fact]
    public void RegisterConfigAdapter_ShouldBeAdaptable()
    {
        _registry.Register<TestConfig, IConfiguration>(config => new DefaultConfiguration());

        var adapted = _registry.Adapt<IConfiguration>(new TestConfig());

        adapted.Should().NotBeNull();
        adapted.Should().BeOfType<DefaultConfiguration>();
    }

    [Fact]
    public void HasConfigAdapter_ExistingType_ShouldReturnTrue()
    {
        _registry.Register<TestConfig, IConfiguration>(config => new DefaultConfiguration());

        _registry.HasAdapter<TestConfig, IConfiguration>().Should().BeTrue();
    }

    [Fact]
    public void HasConfigAdapter_NonExistingType_ShouldReturnFalse()
    {
        _registry.HasAdapter<TestConfig, IConfiguration>().Should().BeFalse();
    }


    [Fact]
    public void RegisterStateMachineAdapter_ShouldBeAdaptable()
    {
        _registry.Register<TestSM, IStateMachine>(sm => new TestStateMachine());

        var adapted = _registry.Adapt<IStateMachine>(new TestSM());

        adapted.Should().NotBeNull();
        adapted.Should().BeOfType<TestStateMachine>();
    }

    [Fact]
    public void HasStateMachineAdapter_ExistingType_ShouldReturnTrue()
    {
        _registry.Register<TestSM, IStateMachine>(sm => new TestStateMachine());

        _registry.HasAdapter<TestSM, IStateMachine>().Should().BeTrue();
    }


    [Fact]
    public void RegisterGraphAdapter_ShouldBeAdaptable()
    {
        _registry.Register<TestGraphImpl, IGraph>(g => new TestGraph());

        var adapted = _registry.Adapt<IGraph>(new TestGraphImpl());

        adapted.Should().NotBeNull();
        adapted.Should().BeOfType<TestGraph>();
    }

    [Fact]
    public void HasGraphAdapter_ExistingType_ShouldReturnTrue()
    {
        _registry.Register<TestGraphImpl, IGraph>(g => new TestGraph());

        _registry.HasAdapter<TestGraphImpl, IGraph>().Should().BeTrue();
    }


    [Fact]
    public void RegisterWithCapabilities_ShouldStoreCapabilities()
    {
        var capabilities = new AdapterCapabilities
        {
            Source = "Test",
            SupportedOperations = new List<string> { "transform", "validate" }
        };

        _registry.Register<TestConfig, IConfiguration>(config => new DefaultConfiguration(), capabilities);

        var stored = _registry.GetCapabilities<TestConfig, IConfiguration>();

        stored.Should().NotBeNull();
        stored!.Source.Should().Be("Test");
        stored.SupportedOperations.Should().Contain("transform");
    }

    [Fact]
    public void GetCapabilities_NonExistingType_ShouldReturnNull()
    {
        var capabilities = _registry.GetCapabilities<TestConfig, IConfiguration>();

        capabilities.Should().BeNull();
    }


    [Fact]
    public void Adapt_Generic_ShouldWorkForAllTypes()
    {
        _registry.Register<TestConfig, IConfiguration>(config => new DefaultConfiguration());
        _registry.Register<TestSM, IStateMachine>(sm => new TestStateMachine());
        _registry.Register<TestGraphImpl, IGraph>(g => new TestGraph());

        var config = _registry.Adapt<IConfiguration>(new TestConfig());
        var sm = _registry.Adapt<IStateMachine>(new TestSM());
        var graph = _registry.Adapt<IGraph>(new TestGraphImpl());

        config.Should().NotBeNull();
        sm.Should().NotBeNull();
        graph.Should().NotBeNull();
    }


    [Fact]
    public void Clear_ShouldRemoveAllAdapters()
    {
        _registry.Register<TestConfig, IConfiguration>(config => new DefaultConfiguration());
        _registry.Clear();

        _registry.HasAdapter<TestConfig, IConfiguration>().Should().BeFalse();
    }


    [Fact]
    public void GetInfo_ShouldReturnRegisteredAdapters()
    {
        _registry.Register<TestConfig, IConfiguration>(config => new DefaultConfiguration());

        var info = _registry.GetInfo();

        info.ConfigAdapters.Should().Contain(typeof(TestConfig).FullName);
        info.TotalCount.Should().Be(1);
    }


    [Fact]
    public void ListAllCapabilities_ShouldReturnAll()
    {
        _registry.Register<TestConfig, IConfiguration>(config => new DefaultConfiguration(),
            new AdapterCapabilities
            {
                Source = "Test",
                SupportedOperations = new List<string> { "transform" }
            });

        var all = _registry.ListAllCapabilities();

        all.Should().HaveCount(1);
    }

    [Fact]
    public void FindByOperation_ShouldReturnMatching()
    {
        _registry.Register<TestConfig, IConfiguration>(config => new DefaultConfiguration(),
            new AdapterCapabilities
            {
                Source = "Test",
                SupportedOperations = new List<string> { "transform" }
            });

        var found = _registry.FindByOperation("transform");

        found.Should().HaveCount(1);
    }

    [Fact]
    public void FindBySource_ShouldReturnMatching()
    {
        _registry.Register<TestConfig, IConfiguration>(config => new DefaultConfiguration(),
            new AdapterCapabilities
            {
                Source = "MySource",
                SupportedOperations = new List<string> { "transform" }
            });

        var found = _registry.FindBySource("MySource");

        found.Should().HaveCount(1);
    }
}


public class TestConfig { }
public class TestSM { }
public class TestGraphImpl { }
