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

    [ProtoContract]
    public class NULL : InvokeParam
    {
        public override object UntypedValue
        {
            get => null;
            set => throw new NotImplementedException();
        }
    }
}
