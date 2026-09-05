#region USINGS
using System;
using System.Collections.Generic;
using Agent.Transformers.Core;
#endregion

namespace Agent.Transformers.Runtime
{
    public class DataEncoder
    {
        private readonly Dictionary<string, int> _labelToIndex = new();
        private readonly Dictionary<int, string> _indexToLabel = new();
        private bool _isFitted;

        public int NumClasses => _labelToIndex.Count;

        public bool IsFitted => _isFitted;

        public void Fit(string[] labels)
        {
            _labelToIndex.Clear();
            _indexToLabel.Clear();

            var uniqueLabels = new HashSet<string>(labels);
            int idx = 0;
            foreach (var label in uniqueLabels)
            {
                _labelToIndex[label] = idx;
                _indexToLabel[idx] = label;
                idx++;
            }

            _isFitted = true;
        }

        public Tensor EncodeOneHot(string[] labels)
        {
            if (!_isFitted)
                throw new InvalidOperationException("DataEncoder not fitted.");

            int n = labels.Length;
            int numClasses = NumClasses;
            var result = new Tensor(new TensorShape3D(n, 1, numClasses));

            for (int i = 0; i < n; i++)
            {
                if (_labelToIndex.TryGetValue(labels[i], out int idx))
                {
                    result[i, 0, idx] = 1.0;
                }
            }

            return result;
        }

        public int[] EncodeLabel(string[] labels)
        {
            if (!_isFitted)
                throw new InvalidOperationException("DataEncoder not fitted.");

            var result = new int[labels.Length];
            for (int i = 0; i < labels.Length; i++)
            {
                result[i] = _labelToIndex.GetValueOrDefault(labels[i], 0);
            }

            return result;
        }

        public string[] Decode(int[] indices)
        {
            if (!_isFitted)
                throw new InvalidOperationException("DataEncoder not fitted.");

            var result = new string[indices.Length];
            for (int i = 0; i < indices.Length; i++)
            {
                result[i] = _indexToLabel.GetValueOrDefault(indices[i], "UNKNOWN");
            }

            return result;
        }
    }
}
