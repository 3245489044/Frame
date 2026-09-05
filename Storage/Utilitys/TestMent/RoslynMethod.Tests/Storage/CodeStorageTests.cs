using StorageFramework;
using InsufficientMemoryException = StorageFramework.InsufficientMemoryException;

namespace RoslynMethod.Tests.Storage;

public class CodeStorageTests
{
    [Fact]
    public void LRUCache_AddAndTryGetValue_ReturnsStoredValue()
    {
        var cache = new LRUCache<string, int>(capacity: 3);

        cache.Add("key1", 100);

        var found = cache.TryGetValue("key1", out var value);
        found.Should().BeTrue();
        value.Should().Be(100);
    }

    [Fact]
    public void LRUCache_TryGetValue_WithNonExistentKey_ReturnsFalse()
    {
        var cache = new LRUCache<string, int>(capacity: 3);

        var found = cache.TryGetValue("nonexistent", out var value);

        found.Should().BeFalse();
        value.Should().Be(0);
    }

    [Fact]
    public void LRUCache_Add_WhenFull_EvictsLeastRecentlyUsed()
    {
        var cache = new LRUCache<string, int>(capacity: 2);

        cache.Add("key1", 1);
        cache.Add("key2", 2);
        cache.Add("key3", 3);

        var found1 = cache.TryGetValue("key1", out _);
        found1.Should().BeFalse();

        var found2 = cache.TryGetValue("key2", out var value2);
        found2.Should().BeTrue();
        value2.Should().Be(2);

        var found3 = cache.TryGetValue("key3", out var value3);
        found3.Should().BeTrue();
        value3.Should().Be(3);
    }

    [Fact]
    public void LRUCache_Remove_ExistingKey_ReturnsTrue()
    {
        var cache = new LRUCache<string, int>(capacity: 3);
        cache.Add("key1", 1);

        var removed = cache.Remove("key1");

        removed.Should().BeTrue();
        cache.TryGetValue("key1", out _).Should().BeFalse();
    }

    [Fact]
    public void LRUCache_Remove_NonExistentKey_ReturnsFalse()
    {
        var cache = new LRUCache<string, int>(capacity: 3);

        var removed = cache.Remove("nonexistent");

        removed.Should().BeFalse();
    }

    [Fact]
    public void LRUCache_Clear_EmptiesCache()
    {
        var cache = new LRUCache<string, int>(capacity: 3);
        cache.Add("key1", 1);
        cache.Add("key2", 2);

        cache.Clear();

        cache.Count.Should().Be(0);
    }

    [Fact]
    public void LRUCache_Count_ReturnsCorrectCount()
    {
        var cache = new LRUCache<string, int>(capacity: 5);
        cache.Add("key1", 1);
        cache.Add("key2", 2);
        cache.Add("key3", 3);

        cache.Count.Should().Be(3);
    }

    [Fact]
    public void LRUCache_Add_UpdatesExistingKey()
    {
        var cache = new LRUCache<string, int>(capacity: 3);
        cache.Add("key1", 1);

        cache.Add("key1", 100);

        cache.TryGetValue("key1", out var value);
        value.Should().Be(100);
    }

    [Fact]
    public void LRUCache_Constructor_WithZeroCapacity_ThrowsArgumentException()
    {
        var action = () => new LRUCache<string, int>(0);

        action.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void LRUCache_Dispose_DoesNotThrow()
    {
        var cache = new LRUCache<string, int>(capacity: 3);

        var action = () => cache.Dispose();

        action.Should().NotThrow();
    }

    [Fact]
    public void MemoryManager_GetCurrentMemoryUsage_ReturnsPositiveValue()
    {
        var memory = MemoryManager.GetCurrentMemoryUsage();

        memory.Should().BeGreaterThan(0);
    }

    [Fact]
    public void MemoryManager_FormatBytes_ReturnsFormattedString()
    {
        var formatted = MemoryManager.FormatBytes(1024);

        formatted.Should().Be("1 KB");
    }

    [Fact]
    public void MemoryManager_FormatBytes_WithLargeValue_ReturnsFormattedString()
    {
        var formatted = MemoryManager.FormatBytes(1048576);

        formatted.Should().Be("1 MB");
    }

    [Fact]
    public void MemoryManager_HasSufficientMemory_WithZeroBytes_ReturnsTrue()
    {
        var result = MemoryManager.HasSufficientMemory(0);

        result.Should().BeTrue();
    }

    [Fact]
    public void InsufficientMemoryException_Constructor_SetsRequiredBytes()
    {
        var ex = new InsufficientMemoryException("Not enough memory", 1024, 512);

        ex.Message.Should().Be("Not enough memory");
        ex.RequiredBytes.Should().Be(1024);
        ex.AvailableBytes.Should().Be(512);
    }

    [Fact]
    public void InsufficientMemoryException_DefaultConstructor_Works()
    {
        var ex = new InsufficientMemoryException("Test error");

        ex.Message.Should().Be("Test error");
        ex.RequiredBytes.Should().Be(0);
    }
}
