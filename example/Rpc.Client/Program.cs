using NetGear.Example.Rpc;
using System;
using System.Net;
using System.Collections.Generic;

namespace Rpc.Client
{
    class Program
    {
        static void Main(string[] args)
        {
            var remote_endpoint = new IPEndPoint(IPAddress.Parse("127.0.0.1"), 5000);

            //var proxy1 = new DataContractProxy(remote_endpoint);
            //var ret1 = proxy1.AddMoney(1, 2);
            //Console.WriteLine("调用IDataContract.AddMoney方法成功，返回值：" + ret1);

            var proxy2 = new TestContractProxy(remote_endpoint);
            var ret2 = proxy2.Get(Guid.NewGuid(), "label", 0.01d, 1);
            Console.WriteLine("调用ITestContract.Get方法成功，返回值：" + ret2);

            var ret3 = proxy2.GetDecimal(0.01m);
            Console.WriteLine("调用ITestContract.GetDecimal方法成功，返回值：" + ret3);

            var ret4 = proxy2.GetId("source", 0.01d, 1, DateTime.Now);
            Console.WriteLine("调用ITestContract.GetId方法成功，返回值：" + ret4);

            var ret5 = proxy2.OutDecimal(0.01m);
            Console.WriteLine("调用ITestContract.OutDecimal方法成功，返回值：" + ret5);

            var ret6 = proxy2.TestLong(1, new List<long> { 0, 1, 2, 3 });
            Console.WriteLine("调用ITestContract.TestLong方法成功，返回值：" + ret6);

            Console.Read();
        }
    }
}
