using NetGear.Core.Listener;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;

namespace NetGear.Rpc.Server
{
    public class RpcServer 
    {
        bool _debug;
        int _bufferSize = 512;
        int _maxConnectionCount = 500;
        IPEndPoint _endPoint;
        BaseListener _listener;
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
