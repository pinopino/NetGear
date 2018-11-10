using ProtoBuf;
using System;
using System.Collections.Generic;

namespace NetGear.Example.Rpc
{
    [ProtoContract]
    public class ComplexResponse
    {
        [ProtoMember(1)]
        public Guid Id { get; set; }

        [ProtoMember(2)]
        public string Label { get; set; }

        [ProtoMember(3)]
        public long Quantity { get; set; }
    }

    public interface ITestContract
    {
        decimal GetDecimal(decimal input);
        bool OutDecimal(decimal val);
        Guid GetId(string source, double weight, int quantity, DateTime dt);
        ComplexResponse Get(Guid id, string label, double weight, long quantity);
        long TestLong(long id1, List<long> id2);
    }
}
