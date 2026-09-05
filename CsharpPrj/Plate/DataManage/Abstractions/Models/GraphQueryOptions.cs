#nullable enable

using System.Collections.Generic;

namespace DataManage.Abstractions.Models
{
    public class GraphQueryOptions
    {
        public int? Limit { get; set; }
        public int? Offset { get; set; }
        public List<(string Field, object Value, QueryOperator Operator)> Filters { get; set; } = new();
        public List<(string Field, SortOrder Order)> SortOptions { get; set; } = new();
        public bool Distinct { get; set; }
    }

    public enum QueryOperator
    {
        Equals,
        NotEquals,
        Contains,
        StartsWith,
        EndsWith,
        GreaterThan,
        LessThan,
        GreaterThanOrEqual,
        LessThanOrEqual,
        In,
        NotIn
    }

    public enum SortOrder
    {
        Ascending,
        Descending
    }

    public class GraphQueryResult<T>
    {
        public List<T> Items { get; set; } = new();
        public long TotalCount { get; set; }
        public bool HasMore => Offset + Items.Count < TotalCount;
        public int? Offset { get; set; }
        public int? Limit { get; set; }
    }
}