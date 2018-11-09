using ProtoBuf;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace NetGear.Rpc
{
    public class ServiceInfo
    {
        public object Instance;
        public Dictionary<int, MethodInfo> Methods;
    }

    public enum MessageType
    {
        MethodInvocation = 1,
        ReturnValues = 2,
        UnknownMethod = 3,
        ThrowException = 4
    }

    [ProtoContract]
    class NULL : InvokeParam
    {
        public override object UntypedValue
        {
            get => null;
            set => throw new NotImplementedException();
        }
    }

    [ProtoContract]
    public class InvokeInfo
    {
        [ProtoMember(1)]
        public ulong ServiceHash;
        [ProtoMember(2)]
        public int MethodIndex;

        private readonly List<InvokeParam> parameters = new List<InvokeParam>();
        [ProtoMember(3)]
        public List<InvokeParam> Parameters { get { return parameters; } }
    }

    [ProtoContract]
    public class InvokeReturn
    {
        [ProtoMember(1)]
        public int ReturnType;

        [ProtoMember(2)]
        public InvokeParam ReturnValue;
    }

    [ProtoContract]
    [ProtoInclude(10, typeof(InvokeParam<Boolean>))]
    [ProtoInclude(11, typeof(InvokeParam<Char>))]
    [ProtoInclude(12, typeof(InvokeParam<SByte>))]
    [ProtoInclude(13, typeof(InvokeParam<Byte>))]
    [ProtoInclude(14, typeof(InvokeParam<Int16>))]
    [ProtoInclude(15, typeof(InvokeParam<UInt16>))]
    [ProtoInclude(16, typeof(InvokeParam<Int32>))]
    [ProtoInclude(17, typeof(InvokeParam<UInt32>))]
    [ProtoInclude(18, typeof(InvokeParam<Int64>))]
    [ProtoInclude(19, typeof(InvokeParam<UInt64>))]
    [ProtoInclude(20, typeof(InvokeParam<Single>))]
    [ProtoInclude(21, typeof(InvokeParam<Double>))]
    [ProtoInclude(22, typeof(InvokeParam<Decimal>))]
    [ProtoInclude(23, typeof(InvokeParam<DateTime>))]
    [ProtoInclude(24, typeof(InvokeParam<String>))]
    [ProtoInclude(25, typeof(InvokeParam<Guid>))]

    [ProtoInclude(26, typeof(InvokeParam<List<Boolean>>))]
    [ProtoInclude(27, typeof(InvokeParam<List<Char>>))]
    [ProtoInclude(28, typeof(InvokeParam<List<SByte>>))]
    [ProtoInclude(29, typeof(InvokeParam<List<Byte>>))]
    [ProtoInclude(30, typeof(InvokeParam<List<Int16>>))]
    [ProtoInclude(31, typeof(InvokeParam<List<UInt16>>))]
    [ProtoInclude(32, typeof(InvokeParam<List<Int32>>))]
    [ProtoInclude(33, typeof(InvokeParam<List<UInt32>>))]
    [ProtoInclude(34, typeof(InvokeParam<List<Int64>>))]
    [ProtoInclude(35, typeof(InvokeParam<List<UInt64>>))]
    [ProtoInclude(36, typeof(InvokeParam<List<Single>>))]
    [ProtoInclude(37, typeof(InvokeParam<List<Double>>))]
    [ProtoInclude(38, typeof(InvokeParam<List<Decimal>>))]
    [ProtoInclude(39, typeof(InvokeParam<List<DateTime>>))]
    [ProtoInclude(40, typeof(InvokeParam<List<String>>))]
    [ProtoInclude(41, typeof(InvokeParam<List<Guid>>))]

    [ProtoInclude(42, typeof(InvokeParam<Char[]>))]
    [ProtoInclude(43, typeof(InvokeParam<SByte[]>))]
    [ProtoInclude(44, typeof(InvokeParam<Byte[]>))]
    [ProtoInclude(45, typeof(InvokeParam<Int16[]>))]
    [ProtoInclude(46, typeof(InvokeParam<UInt16[]>))]
    [ProtoInclude(47, typeof(InvokeParam<Int32[]>))]
    [ProtoInclude(48, typeof(InvokeParam<UInt32[]>))]
    [ProtoInclude(49, typeof(InvokeParam<Int64[]>))]
    [ProtoInclude(50, typeof(InvokeParam<UInt64[]>))]
    [ProtoInclude(51, typeof(InvokeParam<Single[]>))]
    [ProtoInclude(52, typeof(InvokeParam<Double[]>))]
    [ProtoInclude(53, typeof(InvokeParam<Decimal[]>))]
    [ProtoInclude(54, typeof(InvokeParam<DateTime[]>))]
    [ProtoInclude(55, typeof(InvokeParam<String[]>))]
    [ProtoInclude(56, typeof(InvokeParam<Guid[]>))]
    [ProtoInclude(57, typeof(InvokeParam<Boolean[]>))]
    [ProtoInclude(58, typeof(NULL))]
    // https://stackoverflow.com/questions/2678249/in-protobuf-net-how-can-i-pass-an-array-of-type-object-with-objects-of-different
    public abstract class InvokeParam
    {
        public abstract object UntypedValue { get; set; }

        public static InvokeParam<T> Create<T>(T value)
        {
            return new InvokeParam<T> { Value = value };
        }

        public static InvokeParam CreateDynamic(object value)
        {
            var type = value.GetType();
            var arr = type.IsArray;
            var list = type.IsGenericType && (type.GetGenericTypeDefinition() == typeof(List<>));
            switch (Type.GetTypeCode(type))
            {
                // special cases
                case TypeCode.Boolean:
                    {
                        if (arr)
                            return Create((Boolean[])value);
                        if (list)
                            return Create((List<Boolean>)value);
                        return Create((Boolean)value);
                    }
                case TypeCode.Char:
                    {
                        if (arr)
                            return Create((Char[])value);
                        if (list)
                            return Create((List<Char>)value);
                        return Create((Char)value);
                    }
                case TypeCode.SByte:
                    {
                        if (arr)
                            return Create((SByte[])value);
                        if (list)
                            return Create((List<SByte>)value);
                        return Create((SByte)value);
                    }
                case TypeCode.Byte:
                    {
                        if (arr)
                            return Create((Byte[])value);
                        if (list)
                            return Create((List<Byte>)value);
                        return Create((Byte)value);
                    }
                case TypeCode.Int16:
                    {
                        if (arr)
                            return Create((Int16[])value);
                        if (list)
                            return Create((List<Int16>)value);
                        return Create((Int16)value);
                    }
                case TypeCode.UInt16:
                    {
                        if (arr)
                            return Create((UInt16[])value);
                        if (list)
                            return Create((List<UInt16>)value);
                        return Create((UInt16)value);
                    }
                case TypeCode.Int32:
                    {
                        if (arr)
                            return Create((Int32[])value);
                        if (list)
                            return Create((List<Int32>)value);
                        return Create((Int32)value);
                    }
                case TypeCode.UInt32:
                    {
                        if (arr)
                            return Create((UInt32[])value);
                        if (list)
                            return Create((List<UInt32>)value);
                        return Create((UInt32)value);
                    }
                case TypeCode.Int64:
                    {
                        if (arr)
                            return Create((Int64[])value);
                        if (list)
                            return Create((List<Int64>)value);
                        return Create((Int64)value);
                    }
                case TypeCode.UInt64:
                    {
                        if (arr)
                            return Create((UInt64[])value);
                        if (list)
                            return Create((List<UInt64>)value);
                        return Create((UInt64)value);
                    }
                case TypeCode.Single:
                    {
                        if (arr)
                            return Create((Single[])value);
                        if (list)
                            return Create((List<Single>)value);
                        return Create((Single)value);
                    }
                case TypeCode.Double:
                    {
                        if (arr)
                            return Create((Double[])value);
                        if (list)
                            return Create((List<Double>)value);
                        return Create((Double)value);
                    }
                case TypeCode.Decimal:
                    {
                        if (arr)
                            return Create((Decimal[])value);
                        if (list)
                            return Create((List<Decimal>)value);
                        return Create((Decimal)value);
                    }
                case TypeCode.DateTime:
                    {
                        if (arr)
                            return Create((DateTime[])value);
                        if (list)
                            return Create((List<DateTime>)value);
                        return Create((DateTime)value);
                    }
                case TypeCode.String:
                    {
                        if (arr)
                            return Create((String[])value);
                        if (list)
                            return Create((List<String>)value);
                        return Create((String)value);
                    }
                // fallback in case we forget to add one, or it isn't a TypeCode
                default:
                    InvokeParam param = (InvokeParam)Activator.CreateInstance(
                        typeof(InvokeParam<>).MakeGenericType(type));
                    param.UntypedValue = value;
                    return param;
            }
        }
    }

    [ProtoContract]
    public sealed class InvokeParam<T> : InvokeParam
    {
        [ProtoMember(1)]
        public T Value { get; set; }

        public override object UntypedValue
        {
            get { return Value; }
            set { Value = (T)value; }
        }
    }
}
