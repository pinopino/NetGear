using NetGear.Example.Rpc;
using NetGear.Rpc.Server;
using System;

namespace Rpc.Server
{
    public class DataContractImpl : IDataContract
    {
        public long AddMoney(long input1, long input2)
        {
            return input1 + input2;
        }
    }

    class Program
    {
        static void Main(string[] args)
        {
            var server = new RpcServer("127.0.0.1", 5000);

            var simpleContract = new DataContractImpl();
            server.AddService<IDataContract>(simpleContract);

            server.Start();

            Console.WriteLine("按任意键关闭server...");
            Console.ReadLine();
            server.Stop();

            Console.Read();
        }
    }
}
