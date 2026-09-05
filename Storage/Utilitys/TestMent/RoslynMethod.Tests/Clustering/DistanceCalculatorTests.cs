using MathematicalAlgorithms;

namespace RoslynMethod.Tests.Clustering;

public class DistanceCalculatorTests
{
    [Fact]
    public void EuclideanDistance_WithSamePoints_ReturnsZero()
    {
        double[] point1 = [1.0, 2.0, 3.0];
        double[] point2 = [1.0, 2.0, 3.0];

        var distance = DistanceCalculator.EuclideanDistance(point1, point2);

        distance.Should().Be(0.0);
    }

    [Fact]
    public void EuclideanDistance_WithDifferentPoints_ReturnsPositiveDistance()
    {
        double[] point1 = [0.0, 0.0];
        double[] point2 = [3.0, 4.0];

        var distance = DistanceCalculator.EuclideanDistance(point1, point2);

        distance.Should().Be(5.0);
    }

    [Fact]
    public void EuclideanDistance_WithDifferentLengths_ThrowsArgumentException()
    {
        double[] point1 = [1.0, 2.0];
        double[] point2 = [1.0, 2.0, 3.0];

        var action = () => DistanceCalculator.EuclideanDistance(point1, point2);

        action.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ManhattanDistance_WithSamePoints_ReturnsZero()
    {
        double[] point1 = [1.0, 2.0];
        double[] point2 = [1.0, 2.0];

        var distance = DistanceCalculator.ManhattanDistance(point1, point2);

        distance.Should().Be(0.0);
    }

    [Fact]
    public void ManhattanDistance_WithDifferentPoints_ReturnsCorrectDistance()
    {
        double[] point1 = [1.0, 2.0];
        double[] point2 = [4.0, 6.0];

        var distance = DistanceCalculator.ManhattanDistance(point1, point2);

        distance.Should().Be(7.0);
    }

    [Fact]
    public void CosineSimilarity_WithIdenticalVectors_ReturnsOne()
    {
        double[] vector1 = [1.0, 2.0, 3.0];
        double[] vector2 = [1.0, 2.0, 3.0];

        var similarity = DistanceCalculator.CosineSimilarity(vector1, vector2);

        similarity.Should().BeApproximately(1.0, 1e-10);
    }

    [Fact]
    public void CosineSimilarity_WithOrthogonalVectors_ReturnsZero()
    {
        double[] vector1 = [1.0, 0.0];
        double[] vector2 = [0.0, 1.0];

        var similarity = DistanceCalculator.CosineSimilarity(vector1, vector2);

        similarity.Should().BeApproximately(0.0, 1e-10);
    }

    [Fact]
    public void CosineDistance_WithIdenticalVectors_ReturnsZero()
    {
        double[] vector1 = [1.0, 2.0];
        double[] vector2 = [1.0, 2.0];

        var distance = DistanceCalculator.CosineDistance(vector1, vector2);

        distance.Should().BeApproximately(0.0, 1e-10);
    }

    [Fact]
    public void JaccardSimilarity_WithIdenticalSets_ReturnsOne()
    {
        var set1 = new HashSet<int> { 1, 2, 3 };
        var set2 = new HashSet<int> { 1, 2, 3 };

        var similarity = DistanceCalculator.JaccardSimilarity(set1, set2);

        similarity.Should().Be(1.0);
    }

    [Fact]
    public void JaccardSimilarity_WithDisjointSets_ReturnsZero()
    {
        var set1 = new HashSet<int> { 1, 2 };
        var set2 = new HashSet<int> { 3, 4 };

        var similarity = DistanceCalculator.JaccardSimilarity(set1, set2);

        similarity.Should().Be(0.0);
    }

    [Fact]
    public void JaccardSimilarity_WithPartialOverlap_ReturnsCorrectValue()
    {
        var set1 = new HashSet<int> { 1, 2, 3 };
        var set2 = new HashSet<int> { 2, 3, 4 };

        var similarity = DistanceCalculator.JaccardSimilarity(set1, set2);

        similarity.Should().BeApproximately(0.5, 1e-10);
    }

    [Fact]
    public void JaccardDistance_WithIdenticalSets_ReturnsZero()
    {
        var set1 = new HashSet<int> { 1, 2, 3 };
        var set2 = new HashSet<int> { 1, 2, 3 };

        var distance = DistanceCalculator.JaccardDistance(set1, set2);

        distance.Should().Be(0.0);
    }

    [Fact]
    public void HammingDistance_WithIdenticalStrings_ReturnsZero()
    {
        var distance = DistanceCalculator.HammingDistance("hello", "hello");

        distance.Should().Be(0);
    }

    [Fact]
    public void HammingDistance_WithDifferentStrings_ReturnsCorrectCount()
    {
        var distance = DistanceCalculator.HammingDistance("karolin", "kathrin");

        distance.Should().Be(3);
    }

    [Fact]
    public void HammingDistance_WithDifferentLengths_ThrowsArgumentException()
    {
        var action = () => DistanceCalculator.HammingDistance("abc", "abcd");

        action.Should().Throw<ArgumentException>();
    }
}
