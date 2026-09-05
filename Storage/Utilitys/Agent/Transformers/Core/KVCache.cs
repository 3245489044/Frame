#region USINGS
using System;
using System.Collections.Generic;
using MathematicalAlgorithms;
#endregion

namespace Agent.Transformers.Core
{
    public class KVCache
    {
        private static readonly LoopEngine _loopEngine = new();

        private readonly int _maxCacheLen;
        private readonly int _batchSize;
        private readonly int _dimension;
        private readonly int _numLayers;

        private readonly List<Tensor> _kCache;
        private readonly List<Tensor> _vCache;

        public int CurrentLength { get; private set; }

        public int MaxCacheLen => _maxCacheLen;

        public KVCache(
            int batchSize,
            int dimension,
            int numLayers,
            int maxCacheLen = 2048)
        {
            if (batchSize <= 0)
                throw new ArgumentException("batchSize must be positive", nameof(batchSize));
            if (dimension <= 0)
                throw new ArgumentException("dimension must be positive", nameof(dimension));
            if (numLayers <= 0)
                throw new ArgumentException("numLayers must be positive", nameof(numLayers));
            if (maxCacheLen <= 0)
                throw new ArgumentException("maxCacheLen must be positive", nameof(maxCacheLen));

            _batchSize = batchSize;
            _dimension = dimension;
            _numLayers = numLayers;
            _maxCacheLen = maxCacheLen;

            _kCache = new List<Tensor>(numLayers);
            _vCache = new List<Tensor>(numLayers);

            for (int i = 0; i < numLayers; i++)
            {
                _kCache.Add(new Tensor(new TensorShape3D(batchSize, 0, dimension)));
                _vCache.Add(new Tensor(new TensorShape3D(batchSize, 0, dimension)));
            }

            CurrentLength = 0;
        }

        public void Append(int layerIndex, Tensor k, Tensor v)
        {
            if (layerIndex < 0 || layerIndex >= _numLayers)
                throw new IndexOutOfRangeException(
                    $"Layer {layerIndex} out of range [0, {_numLayers})");

            if (k == null) throw new ArgumentNullException(nameof(k));
            if (v == null) throw new ArgumentNullException(nameof(v));

            if (k.BatchSize != _batchSize)
                throw new ArgumentException(
                    $"K batch size {k.BatchSize} != expected {_batchSize}", nameof(k));
            if (v.BatchSize != _batchSize)
                throw new ArgumentException(
                    $"V batch size {v.BatchSize} != expected {_batchSize}", nameof(v));
            if (k.Dimension != _dimension)
                throw new ArgumentException(
                    $"K dimension {k.Dimension} != expected {_dimension}", nameof(k));
            if (v.Dimension != _dimension)
                throw new ArgumentException(
                    $"V dimension {v.Dimension} != expected {_dimension}", nameof(v));

            int newTokens = k.SeqLength;

            if (CurrentLength + newTokens > _maxCacheLen)
            {
                int overflow = CurrentLength + newTokens - _maxCacheLen;
                TrimStart(overflow);
            }

            var currentK = _kCache[layerIndex];
            var currentV = _vCache[layerIndex];

            int oldLen = currentK.SeqLength;
            int newLen = oldLen + newTokens;

            var newK = new Tensor(new TensorShape3D(_batchSize, newLen, _dimension));
            var newV = new Tensor(new TensorShape3D(_batchSize, newLen, _dimension));

            _loopEngine.ExecuteInPlaceSync(
                (OldK: currentK.DataRef, OldV: currentV.DataRef,
                 NewK: newK.DataRef, NewV: newV.DataRef,
                 OldLen: oldLen, Dim: _dimension),
                (ctx, b, s) => {
                    if (s < ctx.OldLen)
                    {
                        for (int d = 0; d < ctx.Dim; d++)
                        {
                            ctx.NewK[b, s, d] = ctx.OldK[b, s, d];
                            ctx.NewV[b, s, d] = ctx.OldV[b, s, d];
                        }
                    }
                },
                new LoopShape(_batchSize, oldLen));

            _loopEngine.ExecuteInPlaceSync(
                (NewK: newK.DataRef, NewV: newV.DataRef,
                 AddK: k.DataRef, AddV: v.DataRef,
                 OldLen: oldLen, Dim: _dimension),
                (ctx, b, s) => {
                    int targetS = ctx.OldLen + s;
                    for (int d = 0; d < ctx.Dim; d++)
                    {
                        ctx.NewK[b, targetS, d] = ctx.AddK[b, s, d];
                        ctx.NewV[b, targetS, d] = ctx.AddV[b, s, d];
                    }
                },
                new LoopShape(_batchSize, newTokens));

            _kCache[layerIndex] = newK;
            _vCache[layerIndex] = newV;
            CurrentLength = newLen;
        }

        public Tensor GetK(int layerIndex)
        {
            if (layerIndex < 0 || layerIndex >= _numLayers)
                throw new IndexOutOfRangeException(
                    $"Layer {layerIndex} out of range [0, {_numLayers})");

            return _kCache[layerIndex];
        }

        public Tensor GetV(int layerIndex)
        {
            if (layerIndex < 0 || layerIndex >= _numLayers)
                throw new IndexOutOfRangeException(
                    $"Layer {layerIndex} out of range [0, {_numLayers})");

            return _vCache[layerIndex];
        }

        public void Reset()
        {
            for (int i = 0; i < _numLayers; i++)
            {
                _kCache[i] = new Tensor(new TensorShape3D(_batchSize, 0, _dimension));
                _vCache[i] = new Tensor(new TensorShape3D(_batchSize, 0, _dimension));
            }
            CurrentLength = 0;
        }

        private void TrimStart(int count)
        {
            if (count <= 0) return;
            count = Math.Min(count, CurrentLength);

            for (int i = 0; i < _numLayers; i++)
            {
                var currentK = _kCache[i];
                var currentV = _vCache[i];
                int newLen = currentK.SeqLength - count;

                var newK = new Tensor(new TensorShape3D(_batchSize, newLen, _dimension));
                var newV = new Tensor(new TensorShape3D(_batchSize, newLen, _dimension));

                _loopEngine.ExecuteInPlaceSync(
                    (OldK: currentK.DataRef, OldV: currentV.DataRef,
                     NewK: newK.DataRef, NewV: newV.DataRef,
                     Count: count, Dim: _dimension),
                    (ctx, b, s) => {
                        int srcS = ctx.Count + s;
                        for (int d = 0; d < ctx.Dim; d++)
                        {
                            ctx.NewK[b, s, d] = ctx.OldK[b, srcS, d];
                            ctx.NewV[b, s, d] = ctx.OldV[b, srcS, d];
                        }
                    },
                    new LoopShape(_batchSize, newLen));

                _kCache[i] = newK;
                _vCache[i] = newV;
            }

            CurrentLength -= count;
        }
    }
}
