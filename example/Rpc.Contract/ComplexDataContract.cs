using ProtoBuf;
using System;
using System.Collections.Generic;

namespace NetGear.Example.Rpc
{
    [ProtoContract]
    public struct ComplexResponse
    {
        [ProtoMember(1)]
        public Guid Id { get; set; }

        [ProtoMember(2)]
        public string Label { get; set; }

        [ProtoMember(3)]
        public long Quantity { get; set; }
    }

    [ProtoContract]
    public struct Complexrrrrrrrr
    {
        [ProtoMember(1)]
        public string Name { get; set; }
    }

    public interface ITestContract
    {
        decimal GetDecimal(decimal input);
        bool OutDecimal(decimal val);

        Guid GetId(string source, double weight, int quantity, DateTime dt);
        ComplexResponse Get(Guid id, string label, double weight, long quantity);
        long TestLong(long id1, List<long> id2);
        ComplexResponse GetItems(Guid id);
    }

    public interface ITestContract23
    {
        Complexrrrrrrrr Get(Guid id, ComplexResponse label, double weight, long quantity);
        Complexrrrrrrrr Get(Guid id, Complexrrrrrrrr label, double weight, long quantity);
        Complexrrrrrrrr Get(Guid id, Complexrrrrrrrr label, Complexrrrrrrrr weight, long quantity);
        void Get2(Guid id, Complexrrrrrrrr label, Complexrrrrrrrr weight, long quantity);
    }
}
