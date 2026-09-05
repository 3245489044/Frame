using CodeRLFramework.VectorSpace;
using Xunit;

namespace CodeRLFramework.Tests
{
    public class VectorSpaceTests
    {
        [Fact]
        public void FunctionVector_GetFullVector_ShouldConcatenateAllVectors()
        {
            var func = new FunctionVector
            {
                SemanticVector = new double[] { 1, 2, 3 },
                StructuralVector = new double[] { 4, 5 },
                RelationVector = new double[] { 6 }
            };

            var full = func.GetFullVector();

            Assert.Equal(6, full.Length);
            Assert.Equal(new double[] { 1, 2, 3, 4, 5, 6 }, full);
        }

        [Fact]
        public void FunctionVector_GetFullVector_WhenVectorsNull_ShouldReturnEmpty()
        {
            var func = new FunctionVector();
            var full = func.GetFullVector();
            Assert.Empty(full);
        }

        [Fact]
        public void FunctionVector_CosineSimilarity_IdenticalVectors_ShouldReturn1()
        {
            var func1 = new FunctionVector
            {
                SemanticVector = new double[] { 1, 2, 3, 4, 5 }
            };
            var func2 = new FunctionVector
            {
                SemanticVector = new double[] { 1, 2, 3, 4, 5 }
            };

            double similarity = func1.CosineSimilarity(func2);

            Assert.Equal(1.0, similarity, 6);
        }

        [Fact]
        public void FunctionVector_CosineSimilarity_OrthogonalVectors_ShouldReturn0()
        {
            var func1 = new FunctionVector
            {
                SemanticVector = new double[] { 1, 0, 0 }
            };
            var func2 = new FunctionVector
            {
                SemanticVector = new double[] { 0, 1, 0 }
            };

            double similarity = func1.CosineSimilarity(func2);

            Assert.Equal(0.0, similarity, 6);
        }

        [Fact]
        public void FunctionVector_CosineSimilarity_DifferentDimensions_ShouldReturn0()
        {
            var func1 = new FunctionVector
            {
                SemanticVector = new double[] { 1, 2, 3 }
            };
            var func2 = new FunctionVector
            {
                SemanticVector = new double[] { 1, 2 }
            };

            double similarity = func1.CosineSimilarity(func2);

            Assert.Equal(0.0, similarity);
        }

        [Fact]
        public void FunctionVector_Clone_ShouldCreateDeepCopy()
        {
            var original = new FunctionVector
            {
                Id = "test_1",
                Name = "TestMethod",
                SemanticVector = new double[] { 1, 2, 3 },
                Parameters = new List<ParameterInfo>
                {
                    new ParameterInfo { Name = "x", Type = "int" }
                },
                Calls = new List<string> { "OtherMethod" }
            };

            var clone = original.Clone();

            original.Name = "Modified";
            original.SemanticVector![0] = 999;
            original.Parameters[0].Name = "modified";
            original.Calls.Add("AnotherMethod");

            Assert.Equal("TestMethod", clone.Name);
            Assert.Equal(1, clone.SemanticVector![0]);
            Assert.Equal("x", clone.Parameters[0].Name);
            Assert.Single(clone.Calls);
        }

        [Fact]
        public void FunctionVectorSpace_AddAndCount_ShouldWork()
        {
            var space = new FunctionVectorSpace();
            Assert.Equal(0, space.Count);

            space.AddFunction(new FunctionVector { Name = "Func1" });
            space.AddFunction(new FunctionVector { Name = "Func2" });

            Assert.Equal(2, space.Count);
        }

        [Fact]
        public void FunctionVectorSpace_SearchByName_ShouldReturnMatchingFunctions()
        {
            var space = new FunctionVectorSpace
            {
                Functions = new List<FunctionVector>
                {
                    new FunctionVector { Name = "Sort", SourceWeight = 0.7 },
                    new FunctionVector { Name = "Filter", SourceWeight = 0.5 },
                    new FunctionVector { Name = "SortedList", SourceWeight = 0.3 }
                }
            };

            var results = space.SearchByName("Sort");

            Assert.Equal(2, results.Count);
            Assert.Contains(results, r => r.Name == "Sort");
            Assert.Contains(results, r => r.Name == "SortedList");
        }

        [Fact]
        public void FunctionVectorSpace_FilterByReturnType_ShouldReturnMatching()
        {
            var space = new FunctionVectorSpace
            {
                Functions = new List<FunctionVector>
                {
                    new FunctionVector { Name = "GetInt", ReturnType = "int" },
                    new FunctionVector { Name = "GetString", ReturnType = "string" },
                    new FunctionVector { Name = "GetAnotherInt", ReturnType = "int" }
                }
            };

            var results = space.FilterByReturnType("int");

            Assert.Equal(2, results.Count);
            Assert.All(results, r => Assert.Equal("int", r.ReturnType));
        }

        [Fact]
        public void FunctionVectorSpace_Fuse_ShouldMergeTwoSpaces()
        {
            var spaceA = new FunctionVectorSpace
            {
                Source = "codebase",
                VectorDimension = 3,
                Functions = new List<FunctionVector>
                {
                    new FunctionVector
                    {
                        Name = "CommonFunc",
                        SemanticVector = new double[] { 1, 0, 0 },
                        Source = "codebase",
                        SourceWeight = 0.7
                    },
                    new FunctionVector
                    {
                        Name = "OnlyInA",
                        SemanticVector = new double[] { 0, 1, 0 },
                        Source = "codebase",
                        SourceWeight = 0.7
                    }
                }
            };

            var spaceB = new FunctionVectorSpace
            {
                Source = "template",
                VectorDimension = 3,
                Functions = new List<FunctionVector>
                {
                    new FunctionVector
                    {
                        Name = "CommonFunc",
                        SemanticVector = new double[] { 0, 0, 1 },
                        Source = "template",
                        SourceWeight = 0.3
                    },
                    new FunctionVector
                    {
                        Name = "OnlyInB",
                        SemanticVector = new double[] { 1, 1, 0 },
                        Source = "template",
                        SourceWeight = 0.3
                    }
                }
            };

            var fused = spaceA.Fuse(spaceB, 0.7, 0.3);

            Assert.Equal(3, fused.Count);

            var common = fused.Functions.First(f => f.Name == "CommonFunc");
            Assert.Equal("fused", common.Source);
            Assert.Equal(0.7 * 1 + 0.3 * 0, common.SemanticVector![0], 6);
            Assert.Equal(0.7 * 0 + 0.3 * 0, common.SemanticVector[1], 6);
            Assert.Equal(0.7 * 0 + 0.3 * 1, common.SemanticVector[2], 6);

            var onlyA = fused.Functions.First(f => f.Name == "OnlyInA");
            Assert.Equal(0.7 * 0, onlyA.SemanticVector![0], 6);
            Assert.Equal(0.7 * 1, onlyA.SemanticVector[1], 6);

            var onlyB = fused.Functions.First(f => f.Name == "OnlyInB");
            Assert.Equal(0.3 * 1, onlyB.SemanticVector![0], 6);
            Assert.Equal(0.3 * 1, onlyB.SemanticVector[1], 6);
        }

        [Fact]
        public void FunctionVectorSpace_SearchByVector_ShouldReturnTopK()
        {
            var space = new FunctionVectorSpace
            {
                Functions = new List<FunctionVector>
                {
                    new FunctionVector { Name = "A", SemanticVector = new double[] { 1, 0, 0 } },
                    new FunctionVector { Name = "B", SemanticVector = new double[] { 0, 1, 0 } },
                    new FunctionVector { Name = "C", SemanticVector = new double[] { 0, 0, 1 } },
                    new FunctionVector { Name = "D", SemanticVector = new double[] { 0.9, 0.1, 0 } }
                }
            };

            var query = new double[] { 1, 0, 0 };
            var results = space.SearchByVector(query, topK: 2);

            Assert.Equal(2, results.Count);
            Assert.Equal("A", results[0].func.Name);
            Assert.Equal("D", results[1].func.Name);
        }

        [Fact]
        public void FunctionVectorSpace_GetCompatibleFunctions_ShouldMatchTypes()
        {
            var space = new FunctionVectorSpace
            {
                Functions = new List<FunctionVector>
                {
                    new FunctionVector
                    {
                        Name = "Process",
                        ReturnType = "int",
                        Parameters = new List<ParameterInfo>
                        {
                            new ParameterInfo { Name = "x", Type = "int" },
                            new ParameterInfo { Name = "y", Type = "string" }
                        }
                    },
                    new FunctionVector
                    {
                        Name = "Other",
                        ReturnType = "int",
                        Parameters = new List<ParameterInfo>
                        {
                            new ParameterInfo { Name = "a", Type = "int" }
                        }
                    }
                }
            };

            var compatible = space.GetCompatibleFunctions("int", new List<string> { "int", "string" });

            Assert.Single(compatible);
            Assert.Equal("Process", compatible[0].Name);
        }
    }
}
