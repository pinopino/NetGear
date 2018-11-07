using System;
using System.Collections.Generic;

namespace NetGear.Example.Rpc
{
    public struct ComplexResponse
    {
        public Guid Id { get; set; }
        public string Label { get; set; }
        public long Quantity { get; set; }
    }

    public interface ITestContract
    {
        decimal GetDecimal(decimal input);
        bool OutDecimal(decimal val);

        Guid GetId(string source, double weight, int quantity, DateTime dt);
        ComplexResponse Get(Guid id, string label, double weight, long quantity);
        long TestLong(long id1, List<long> id2);
        List<string> GetItems(Guid id);
    }
}
