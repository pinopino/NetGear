using NetGear.Core.Common;
using System;
using System.Collections.Generic;

namespace NetGear.Rpc.Client
{
    public class RpcClient
    {
        bool _debug;
        static ObjectPool<RpcConnection> _connectionPool;
        static Dictionary<Type, byte> _parameterTypes;

        public RpcClient(Type serviceType, string address, int port)
        {
            _debug = false;
            var num = Math.Min(Environment.ProcessorCount, 16);
            var count = 0;
            _connectionPool = new ObjectPool<RpcConnection>(num * 4, num, pool => new RpcConnection(pool, ++count, address, port, 256, _debug));
        }

        public object InvokeMethod(ulong hash, int index, params object[] parameters)
        {
            using (var conn = _connectionPool.Get())
            {
                conn.Connect();

                var invoke_info = new InvokeInfo
                {
                    ServiceHash = hash,
                    MethodIndex = index
                };
                for (int i = 0; i < parameters.Length; i++)
                {
                    invoke_info.Parameters.Add(InvokeParam.CreateDynamic(parameters[i]));
                }
                conn.Write(invoke_info).Wait();

                // Read the result of the invocation.
                var retObj = conn.ReadObject<InvokeReturn>().Result;
                if (retObj.ReturnType == (int)MessageType.UnknownMethod)
                    throw new Exception("Unknown method.");
                if (retObj.ReturnType == (int)MessageType.ThrowException)
                    throw (Exception)retObj.ReturnValue.UntypedValue;

                return retObj.ReturnValue.UntypedValue;
            }
        }

        public byte GetParameterType(Type type)
        {
            InitializeParamTypes();
            if (_parameterTypes.ContainsKey(type))
                return _parameterTypes[type];
            return ParameterTypes.Unknown;
        }

        private void InitializeParamTypes()
        {
            if (_parameterTypes == null)
            {
                _parameterTypes = new Dictionary<Type, byte>();
                _parameterTypes.Add(typeof(bool), ParameterTypes.Bool);
                _parameterTypes.Add(typeof(byte), ParameterTypes.Byte);
                _parameterTypes.Add(typeof(sbyte), ParameterTypes.SByte);
                _parameterTypes.Add(typeof(char), ParameterTypes.Char);
                _parameterTypes.Add(typeof(decimal), ParameterTypes.Decimal);
                _parameterTypes.Add(typeof(double), ParameterTypes.Double);
                _parameterTypes.Add(typeof(float), ParameterTypes.Float);
                _parameterTypes.Add(typeof(int), ParameterTypes.Int);
                _parameterTypes.Add(typeof(uint), ParameterTypes.UInt);
                _parameterTypes.Add(typeof(long), ParameterTypes.Long);
                _parameterTypes.Add(typeof(ulong), ParameterTypes.ULong);
                _parameterTypes.Add(typeof(short), ParameterTypes.Short);
                _parameterTypes.Add(typeof(ushort), ParameterTypes.UShort);
                _parameterTypes.Add(typeof(string), ParameterTypes.String);
                _parameterTypes.Add(typeof(byte[]), ParameterTypes.ByteArray);
                _parameterTypes.Add(typeof(char[]), ParameterTypes.CharArray);
                _parameterTypes.Add(typeof(Type), ParameterTypes.Type);
                _parameterTypes.Add(typeof(Guid), ParameterTypes.Guid);
                _parameterTypes.Add(typeof(DateTime), ParameterTypes.DateTime);

                _parameterTypes.Add(typeof(bool[]), ParameterTypes.ArrayBool);
                _parameterTypes.Add(typeof(sbyte[]), ParameterTypes.ArraySByte);
                _parameterTypes.Add(typeof(decimal[]), ParameterTypes.ArrayDecimal);
                _parameterTypes.Add(typeof(double[]), ParameterTypes.ArrayDouble);
                _parameterTypes.Add(typeof(float[]), ParameterTypes.ArrayFloat);
                _parameterTypes.Add(typeof(int[]), ParameterTypes.ArrayInt);
                _parameterTypes.Add(typeof(uint[]), ParameterTypes.ArrayUInt);
                _parameterTypes.Add(typeof(long[]), ParameterTypes.ArrayLong);
                _parameterTypes.Add(typeof(ulong[]), ParameterTypes.ArrayULong);
                _parameterTypes.Add(typeof(short[]), ParameterTypes.ArrayShort);
                _parameterTypes.Add(typeof(ushort[]), ParameterTypes.ArrayUShort);
                _parameterTypes.Add(typeof(string[]), ParameterTypes.ArrayString);
                _parameterTypes.Add(typeof(Type[]), ParameterTypes.ArrayType);
                _parameterTypes.Add(typeof(Guid[]), ParameterTypes.ArrayGuid);
                _parameterTypes.Add(typeof(DateTime[]), ParameterTypes.ArrayDateTime);
            }
        }
    }
}
