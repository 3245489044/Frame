#region USINGS
using System;
using System.Threading;
using System.Threading.Tasks;
using MathematicalAlgorithms;
using Agent.AgentFlow.Config;
using Agent.AgentFlow.Interfaces;
using Agent.Transformers.Core;
#endregion

namespace Agent.AgentFlow.Pipeline
{
    public class AgentPipeline : IAgentPipeline
    {
        #region 私有字段

        private readonly AgentFlowConfig _config;
        private readonly DataPipeline _dataPipeline;
        private readonly FeaturePipeline _featurePipeline;
        private readonly ModelPipeline _modelPipeline;
        private readonly OutputPipeline _outputPipeline;

        #endregion

        #region 构造函数

        public AgentPipeline(AgentFlowConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _dataPipeline = new DataPipeline(config);
            _featurePipeline = new FeaturePipeline(config);
            _modelPipeline = new ModelPipeline(config);
            _outputPipeline = new OutputPipeline(config, _dataPipeline);
        }

        #endregion

        #region 公共属性

        public IDataPipeline Data => _dataPipeline;

        public IFeaturePipeline Features => _featurePipeline;

        public IModelPipeline Model => _modelPipeline;

        public IOutputPipeline Output => _outputPipeline;

        #endregion

        #region 执行方法

        public Tensor Execute(Tensor input, IProgress<ComputationProgress>? progress = null)
        {
            progress?.Report(new ComputationProgress(0, "Data preprocessing..."));

            var normalized = _dataPipeline.Normalize(input);
            progress?.Report(new ComputationProgress(25, "Feature extraction..."));

            var features = _featurePipeline.Extract(normalized);
            progress?.Report(new ComputationProgress(50, "Model inference..."));

            var output = _modelPipeline.Forward(features, progress);
            progress?.Report(new ComputationProgress(75, "Output processing..."));

            var result = _outputPipeline.Denormalize(output);
            progress?.Report(new ComputationProgress(100, "Complete"));

            return result;
        }

        public async Task<Tensor> ExecuteAsync(Tensor input,
            IProgress<ComputationProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Execute(input, progress);
            }, cancellationToken);
        }

        #endregion
    }
}
