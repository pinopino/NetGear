using NetGear.Core.Listener;
using ProtoBuf;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading;

namespace NetGear.Rpc.Server
{
    public class RpcServer 
    {
        bool _debug;
        int _bufferSize = 512;
        int _maxConnectionCount = 500;
        IPEndPoint _endPoint;
        BaseListener _listener;
        static volatile int proto_inited = 0;
        internal Dictionary<ulong, ServiceInfo> Services;

        public RpcServer(string address, int port, bool debug = false)
        {
            _debug = debug;
            _listener = new RpcListener(_maxConnectionCount, _bufferSize, this, debug);
            _endPoint = new IPEndPoint(IPAddress.Parse(address), port);
            Services = new Dictionary<ulong, ServiceInfo>();
        }

        public void Start()
        {
            ServerStarting();
            _listener.Start(_endPoint);
            ServerStarted();
        }

        public void Stop()
        {
            ServerStopping();
            _listener.Stop();
            ServerStopped();
        }

        private void ServerStarting()
        {
            Console.WriteLine("server开启中...");
        }

        private void ServerStarted()
        {
            Console.WriteLine("server启动成功");
        }

        private void ServerStopping()
        {
            Console.WriteLine("server关闭中...");
        }

        private void ServerStopped()
        {
            Console.WriteLine("server已关闭");
        }

        /// <summary>
        /// Add this service implementation to the host.
        /// </summary>
        /// <typeparam name="TService"></typeparam>
        /// <param name="service">The singleton implementation.</param>
        public void AddService<TService>(TService service) where TService : class
        {
            var serviceType = typeof(TService);
            if (!serviceType.IsInterface)
                throw new ArgumentException("TService must be an interface.", "TService");
            var serviceKey = CalculateHash(serviceType.FullName);
            if (Services.ContainsKey(serviceKey))
                throw new Exception("Service already added. Only one instance allowed.");

            var methods = CreateMethodMap(serviceType);
            var value = new ServiceInfo { Instance = service, Methods = methods };
            Services.Add(serviceKey, value);
            InitForProto(serviceType);
        }

        private void InitForProto(Type serviceType)
        {
            if (Interlocked.Exchange(ref proto_inited, 1) == 0)
            {
                #region 基础类型
                int index = 10;
                ProtoBuf.Meta.RuntimeTypeModel.Default[typeof(InvokeParam)]
                    .AddSubType(index++, typeof(InvokeParam<Boolean>))
                    .AddSubType(index++, typeof(InvokeParam<Char>))
                    .AddSubType(index++, typeof(InvokeParam<SByte>))
                    .AddSubType(index++, typeof(InvokeParam<Byte>))
                    .AddSubType(index++, typeof(InvokeParam<Int16>))
                    .AddSubType(index++, typeof(InvokeParam<UInt16>))
                    .AddSubType(index++, typeof(InvokeParam<Int32>))
                    .AddSubType(index++, typeof(InvokeParam<UInt32>))
                    .AddSubType(index++, typeof(InvokeParam<Int64>))
                    .AddSubType(index++, typeof(InvokeParam<UInt64>))
                    .AddSubType(index++, typeof(InvokeParam<Single>))
                    .AddSubType(index++, typeof(InvokeParam<Double>))
                    .AddSubType(index++, typeof(InvokeParam<Decimal>))
                    .AddSubType(index++, typeof(InvokeParam<DateTime>))
                    .AddSubType(index++, typeof(InvokeParam<String>))
                    .AddSubType(index++, typeof(InvokeParam<Guid>))

                    .AddSubType(index++, typeof(InvokeParam<List<Boolean>>))
                    .AddSubType(index++, typeof(InvokeParam<List<Char>>))
                    .AddSubType(index++, typeof(InvokeParam<List<SByte>>))
                    .AddSubType(index++, typeof(InvokeParam<List<Byte>>))
                    .AddSubType(index++, typeof(InvokeParam<List<Int16>>))
                    .AddSubType(index++, typeof(InvokeParam<List<UInt16>>))
                    .AddSubType(index++, typeof(InvokeParam<List<Int32>>))
                    .AddSubType(index++, typeof(InvokeParam<List<UInt32>>))
                    .AddSubType(index++, typeof(InvokeParam<List<Int64>>))
                    .AddSubType(index++, typeof(InvokeParam<List<UInt64>>))
                    .AddSubType(index++, typeof(InvokeParam<List<Single>>))
                    .AddSubType(index++, typeof(InvokeParam<List<Double>>))
                    .AddSubType(index++, typeof(InvokeParam<List<Decimal>>))
                    .AddSubType(index++, typeof(InvokeParam<List<DateTime>>))
                    .AddSubType(index++, typeof(InvokeParam<List<String>>))
                    .AddSubType(index++, typeof(InvokeParam<List<Guid>>))

                    .AddSubType(index++, typeof(InvokeParam<Boolean[]>))
                    .AddSubType(index++, typeof(InvokeParam<Char[]>))
                    .AddSubType(index++, typeof(InvokeParam<SByte[]>))
                    .AddSubType(index++, typeof(InvokeParam<Byte[]>))
                    .AddSubType(index++, typeof(InvokeParam<Int16[]>))
                    .AddSubType(index++, typeof(InvokeParam<UInt16[]>))
                    .AddSubType(index++, typeof(InvokeParam<Int32[]>))
                    .AddSubType(index++, typeof(InvokeParam<UInt32[]>))
                    .AddSubType(index++, typeof(InvokeParam<Int64[]>))
                    .AddSubType(index++, typeof(InvokeParam<UInt64[]>))
                    .AddSubType(index++, typeof(InvokeParam<Single[]>))
                    .AddSubType(index++, typeof(InvokeParam<Double[]>))
                    .AddSubType(index++, typeof(InvokeParam<Decimal[]>))
                    .AddSubType(index++, typeof(InvokeParam<DateTime[]>))
                    .AddSubType(index++, typeof(InvokeParam<String[]>))
                    .AddSubType(index++, typeof(InvokeParam<Guid[]>))

                    .AddSubType(index++, typeof(InvokeParam<NULL>));
                #endregion

                var index_for_prototype = 100;
                var types = serviceType.Assembly.ExportedTypes;
                foreach (var type in types.OrderBy(p => p.Name))
                {
                    if (!type.IsInterface)
                    {
                        if (Attribute.GetCustomAttribute(type, typeof(ProtoContractAttribute)) != null)
                        {
                            ProtoBuf.Meta.RuntimeTypeModel.Default[typeof(InvokeParam)]
                                .AddSubType(index_for_prototype++, typeof(InvokeParam<>).MakeGenericType(type));
                        }
                    }
                }
            }
        }

        private Dictionary<int, MethodInfo> CreateMethodMap(Type serviceType)
        {
            var ret = new Dictionary<int, MethodInfo>();
            var methods = serviceType
               .GetMethods(BindingFlags.Instance | BindingFlags.Public)
               .OrderBy(p => (p.Name + "|" + string.Join("|", p.GetParameters().Select(k => k.ParameterType.Name))));

            var index = 0;
            foreach (var item in methods)
            {
                ret.Add(++index, item);
            }

            return ret;
        }

        private ulong CalculateHash(string str)
        {
            var hashedValue = 3074457345618258791ul;
            for (var i = 0; i < str.Length; i++)
            {
                hashedValue += str[i];
                hashedValue *= 3074457345618258799ul;
            }
            return hashedValue;
        }
    }
}
