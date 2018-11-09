using NetGear.Example.Rpc;
using NetGear.Rpc.Server;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Rpc.Server
{
    public class DataContractImpl : IDataContract
    {
        public long AddMoney(int input1, long input2)
        {
            return input1 + input2;
        }
    }

    public class ComplexContractImpl : ITestContract
    {
        public ComplexResponse Get(Guid id, string label, double weight, long quantity)
        {
            return new ComplexResponse { Id = Guid.NewGuid(), Label = "abc", Quantity = 1 };
        }

        public decimal GetDecimal(decimal input)
        {
            return input + 1;
        }

        public Guid GetId(string source, double weight, int quantity, DateTime dt)
        {
            return Guid.NewGuid();
        }

        public List<string> GetItems(Guid id)
        {
            return new List<string> { "a", "b", "c" };
        }

        public bool OutDecimal(decimal val)
        {
            return false;
        }

        public long TestLong(long id1, List<long> id2)
        {
            return id2.Select(p => p + id1).Sum();
        }
    }

    class Program
    {
        static void Main(string[] args)
        {
            var server = new RpcServer("127.0.0.1", 5000);

            var simpleContract = new DataContractImpl();
            var complexContract = new ComplexContractImpl();

            server.AddService<IDataContract>(simpleContract);
            Console.WriteLine("注册服务：IDataContract成功");
            server.AddService<ITestContract>(complexContract);
            Console.WriteLine("注册服务：ITestContract成功");

            server.Start();

            Console.WriteLine("按任意键关闭server...");
            Console.ReadLine();
            server.Stop();

            Console.Read();
        }
    }
}
