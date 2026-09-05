using CodeRLFramework.Input;
using Xunit;

namespace CodeRLFramework.Tests
{
    public class InputTests
    {
        [Fact]
        public void WeightConfig_DefaultValues_ShouldBeValid()
        {
            var config = new WeightConfig();
            Assert.True(config.IsValid());
            Assert.Equal(0.7, config.CodebaseWeight);
            Assert.Equal(0.3, config.TemplateWeight);
            Assert.Equal(0.1, config.ExplorationRate);
            Assert.Equal(1.0, config.Temperature);
        }

        [Fact]
        public void WeightConfig_GetNormalizedWeights_ShouldNormalizeToSum1()
        {
            var config = new WeightConfig
            {
                CodebaseWeight = 0.8,
                TemplateWeight = 0.4
            };

            var (alpha, beta) = config.GetNormalizedWeights();

            Assert.Equal(0.8 / 1.2, alpha, 6);
            Assert.Equal(0.4 / 1.2, beta, 6);
            Assert.Equal(1.0, alpha + beta, 6);
        }

        [Fact]
        public void WeightConfig_GetNormalizedWeights_WhenDisabled_ShouldReturnRaw()
        {
            var config = new WeightConfig
            {
                CodebaseWeight = 0.8,
                TemplateWeight = 0.4,
                NormalizeWeights = false
            };

            var (alpha, beta) = config.GetNormalizedWeights();

            Assert.Equal(0.8, alpha);
            Assert.Equal(0.4, beta);
        }

        [Fact]
        public void WeightConfig_GetNormalizedWeights_WhenSumZero_ShouldReturnEqual()
        {
            var config = new WeightConfig
            {
                CodebaseWeight = 0,
                TemplateWeight = 0
            };

            var (alpha, beta) = config.GetNormalizedWeights();

            Assert.Equal(0.5, alpha);
            Assert.Equal(0.5, beta);
        }

        [Fact]
        public void TemplateInput_DefaultValues_ShouldBeCorrect()
        {
            var template = new TemplateInput();
            Assert.Equal("// TODO", template.Placeholder);
            Assert.Equal(0.3, template.Weight);
            Assert.True(template.PreserveStructure);
        }

        [Fact]
        public void TemplateInput_HasPlaceholder_WhenContainsPlaceholder_ShouldReturnTrue()
        {
            var template = new TemplateInput
            {
                Code = "public void Test() { // TODO: implement }",
                Placeholder = "// TODO"
            };

            Assert.True(template.HasPlaceholder());
        }

        [Fact]
        public void TemplateInput_HasPlaceholder_WhenNoPlaceholder_ShouldReturnFalse()
        {
            var template = new TemplateInput
            {
                Code = "public void Test() { }",
                Placeholder = "// TODO"
            };

            Assert.False(template.HasPlaceholder());
        }

        [Fact]
        public void CodebaseInput_DefaultValues_ShouldBeCorrect()
        {
            var input = new CodebaseInput();
            Assert.Equal(0.7, input.Weight);
            Assert.True(input.Recursive);
            Assert.Equal("*.cs", input.FilePattern);
            Assert.Empty(input.SourceFiles);
            Assert.Empty(input.FileContents);
        }

        [Fact]
        public void CodebaseInput_GetAllCode_WhenNoFiles_ShouldReturnEmpty()
        {
            var input = new CodebaseInput();
            var codes = input.GetAllCode();
            Assert.Empty(codes);
        }
    }
}
