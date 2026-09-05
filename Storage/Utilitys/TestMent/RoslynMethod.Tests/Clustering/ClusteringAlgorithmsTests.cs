using MathematicalAlgorithms;

namespace RoslynMethod.Tests.Clustering;

public class ClusteringAlgorithmsTests
{
    private readonly List<EnhancedCodeElement> _testElements = new()
    {
        new EnhancedCodeElement("1", "MethodA", ElementType.Method, 1, "TestClass")
        {
            ComplexityScore = 5, LinesOfCode = 10,
            ParameterCount = 1, BodyLines = 8
        },
        new EnhancedCodeElement("2", "MethodB", ElementType.Method, 15, "TestClass")
        {
            ComplexityScore = 8, LinesOfCode = 20,
            ParameterCount = 2, BodyLines = 18
        },
        new EnhancedCodeElement("3", "MethodC", ElementType.Method, 30, "TestClass")
        {
            ComplexityScore = 3, LinesOfCode = 5,
            ParameterCount = 0, BodyLines = 4
        },
        new EnhancedCodeElement("4", "MethodD", ElementType.Method, 40, "TestClass")
        {
            ComplexityScore = 10, LinesOfCode = 30,
            ParameterCount = 3, BodyLines = 28
        },
        new EnhancedCodeElement("5", "MethodE", ElementType.Method, 55, "TestClass")
        {
            ComplexityScore = 2, LinesOfCode = 3,
            ParameterCount = 0, BodyLines = 2
        },
        new EnhancedCodeElement("6", "MethodF", ElementType.Method, 60, "TestClass")
        {
            ComplexityScore = 7, LinesOfCode = 15,
            ParameterCount = 1, BodyLines = 13
        },
    };

    [Fact]
    public async Task KMeansClustering_AnalyzeAsync_WithValidData_ReturnsClusters()
    {
        var clustering = new KMeansClustering<EnhancedCodeElement>();
        var result = await clustering.AnalyzeAsync(_testElements, maxClusters: 3);

        result.Should().NotBeNull();
        result.Clusters.Should().NotBeEmpty();
        result.Items.Count.Should().Be(_testElements.Count);
    }

    [Fact]
    public async Task KMeansClustering_AnalyzeAsync_WithKEquals2_ReturnsTwoClusters()
    {
        var clustering = new KMeansClustering<EnhancedCodeElement>();
        var result = await clustering.AnalyzeAsync(_testElements, maxClusters: 2);

        result.Clusters.Count.Should().Be(2);
    }

    [Fact]
    public async Task KMeansClustering_AnalyzeAsync_WithEmptyData_ThrowsException()
    {
        var clustering = new KMeansClustering<EnhancedCodeElement>();

        var result = await clustering.AnalyzeAsync(new List<EnhancedCodeElement>(), maxClusters: 2);

        result.Error.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task KMeansClustering_AnalyzeAsync_WithMaxClustersLessThan2_ReturnsError()
    {
        var clustering = new KMeansClustering<EnhancedCodeElement>();
        var result = await clustering.AnalyzeAsync(_testElements, maxClusters: 1);

        result.Error.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task HierarchicalClustering_AnalyzeAsync_WithValidData_ReturnsClusters()
    {
        var clustering = new HierarchicalClustering<EnhancedCodeElement>();
        var result = await clustering.AnalyzeAsync(_testElements, maxClusters: 3);

        result.Should().NotBeNull();
        result.Clusters.Should().NotBeEmpty();
    }

    [Fact]
    public async Task HierarchicalClustering_AnalyzeAsync_WithEmptyData_ReturnsError()
    {
        var clustering = new HierarchicalClustering<EnhancedCodeElement>();

        var result = await clustering.AnalyzeAsync(new List<EnhancedCodeElement>(), maxClusters: 2);

        result.Error.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void DistanceMatrixBuilder_BuildDistanceMatrix_ReturnsCorrectSize()
    {
        var builder = new DistanceMatrixBuilder(DistanceMetricType.Euclidean);

        var matrix = builder.BuildDistanceMatrix(_testElements);

        matrix.GetLength(0).Should().Be(_testElements.Count);
        matrix.GetLength(1).Should().Be(_testElements.Count);
    }

    [Fact]
    public void DistanceMatrixBuilder_BuildDistanceList_ReturnsDistances()
    {
        var builder = new DistanceMatrixBuilder(DistanceMetricType.Euclidean);

        var distances = builder.BuildDistanceList(_testElements);

        distances.Should().NotBeEmpty();
    }

    [Fact]
    public async Task ClusterAnalyzer_AnalyzeClusters_ReturnsAnalyzedClusters()
    {
        var kmeans = new KMeansClustering<EnhancedCodeElement>();
        var mathResult = await kmeans.AnalyzeAsync(_testElements, maxClusters: 2);
        var analyzer = new ClusterAnalyzer();

        var clusters = analyzer.AnalyzeClusters(mathResult.Clusters, _testElements);

        clusters.Should().NotBeEmpty();
        clusters.Should().AllSatisfy(c => c.Cohesion.Should().BeGreaterThan(0));
    }

    [Fact]
    public void CodeElementFeatureExtractor_ExtractFeatures_ReturnsFeatureVector()
    {
        var extractor = new CodeElementFeatureExtractor();
        var element = _testElements[0];

        var features = extractor.ExtractFeatures(element);

        features.Should().NotBeEmpty();
        features.Length.Should().Be(12);
    }

    [Fact]
    public void CodeElementFeatureExtractor_ExtractFeatures_WithNull_ReturnsEmpty()
    {
        var extractor = new CodeElementFeatureExtractor();

        var features = extractor.ExtractFeatures(null!);

        features.Should().BeEmpty();
    }

    [Fact]
    public void VectorMath_EuclideanDistance_ReturnsCorrectDistance()
    {
        double[] a = [1.0, 2.0, 3.0];
        double[] b = [1.0, 2.0, 3.0];

        var distance = VectorMath.EuclideanDistance(a, b);

        distance.Should().Be(0.0);
    }

    [Fact]
    public void VectorMath_ManhattanDistance_ReturnsCorrectDistance()
    {
        double[] a = [1.0, 2.0];
        double[] b = [4.0, 6.0];

        var distance = VectorMath.ManhattanDistance(a, b);

        distance.Should().Be(7.0);
    }

    [Fact]
    public void VectorMath_CosineDistance_ReturnsCorrectDistance()
    {
        double[] a = [1.0, 0.0];
        double[] b = [0.0, 1.0];

        var distance = VectorMath.CosineDistance(a, b);

        distance.Should().Be(1.0);
    }

    [Fact]
    public void VectorMath_Normalize_ReturnsNormalizedVector()
    {
        double[] vector = [3.0, 4.0];

        var normalized = VectorMath.Normalize(vector);

        normalized[0].Should().BeApproximately(0.6, 1e-10);
        normalized[1].Should().BeApproximately(0.8, 1e-10);
    }

    [Fact]
    public void VectorMath_DotProduct_ReturnsCorrectValue()
    {
        double[] a = [1.0, 2.0, 3.0];
        double[] b = [4.0, 5.0, 6.0];

        var result = VectorMath.DotProduct(a, b);

        result.Should().Be(32.0);
    }

    [Fact]
    public void VectorOperations_CalculateCentroid_ReturnsCorrectCentroid()
    {
        double[][] vectors = [[1.0, 2.0], [3.0, 4.0], [5.0, 6.0]];

        var centroid = VectorOperations.CalculateCentroid(vectors);

        centroid[0].Should().Be(3.0);
        centroid[1].Should().Be(4.0);
    }

    [Fact]
    public void VectorOperations_StandardizeFeatures_ReturnsStandardizedFeatures()
    {
        var features = new List<double[]>
        {
            new double[] { 1.0, 2.0 },
            new double[] { 3.0, 4.0 },
            new double[] { 5.0, 6.0 }
        };

        var standardized = VectorOperations.StandardizeFeatures(features);

        standardized.Should().HaveCount(3);
        standardized[0].Should().HaveCount(2);
    }

    [Fact]
    public void MathUtility_WeightedAverage_ReturnsCorrectValue()
    {
        var utility = new MathUtility();
        double[] values = [1.0, 2.0, 3.0];
        double[] weights = [0.2, 0.3, 0.5];

        var result = utility.WeightedAverage(values, weights);

        result.Should().BeApproximately(2.3, 1e-10);
    }

    [Fact]
    public void MathUtility_StandardDeviation_ReturnsCorrectValue()
    {
        var utility = new MathUtility();
        double[] values = [1.0, 2.0, 3.0, 4.0, 5.0];

        var result = utility.StandardDeviation(values);

        result.Should().BeGreaterThan(0);
    }

    [Fact]
    public void MathUtility_Percentile_ReturnsCorrectValue()
    {
        var utility = new MathUtility();
        double[] values = [1.0, 2.0, 3.0, 4.0, 5.0];

        var median = utility.Percentile(values, 50);

        median.Should().Be(3.0);
    }

    [Fact]
    public void DistanceCalculatorFactory_Create_WithEuclidean_ReturnsCalculator()
    {
        var calculator = DistanceCalculatorFactory.Create(DistanceMetricType.Euclidean);

        calculator.Should().NotBeNull();
        calculator.MetricType.Should().Be(DistanceMetricType.Euclidean);
    }

    [Fact]
    public void DistanceCalculatorFactory_Create_WithManhattan_ReturnsCalculator()
    {
        var calculator = DistanceCalculatorFactory.Create(DistanceMetricType.Manhattan);

        calculator.Should().NotBeNull();
        calculator.MetricType.Should().Be(DistanceMetricType.Manhattan);
    }

    [Fact]
    public void EuclideanDistanceCalculator_Calculate_ReturnsCorrectDistance()
    {
        var calculator = new EuclideanDistanceCalculator();
        double[] a = [0.0, 0.0];
        double[] b = [3.0, 4.0];

        var distance = calculator.Calculate(a, b);

        distance.Should().Be(5.0);
    }

    [Fact]
    public void ManhattanDistanceCalculator_Calculate_ReturnsCorrectDistance()
    {
        var calculator = new ManhattanDistanceCalculator();
        double[] a = [1.0, 2.0];
        double[] b = [4.0, 6.0];

        var distance = calculator.Calculate(a, b);

        distance.Should().Be(7.0);
    }

    [Fact]
    public void CosineDistanceCalculator_Calculate_ReturnsCorrectDistance()
    {
        var calculator = new CosineDistanceCalculator();
        double[] a = [1.0, 0.0];
        double[] b = [0.0, 1.0];

        var distance = calculator.Calculate(a, b);

        distance.Should().Be(1.0);
    }
}
