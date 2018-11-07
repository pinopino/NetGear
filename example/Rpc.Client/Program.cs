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

            var proxy1 = new DataContractProxy(remote_endpoint);
            var ret1 = proxy1.AddMoney(1, 2);
            Console.WriteLine("调用IDataContract.AddMoney方法成功，返回值：" + ret1);

            var proxy2 = new TestContractProxy(remote_endpoint);
            var ret2 = proxy2.Get(Guid.NewGuid(), "label", 0.01d, 1);
            Console.WriteLine("调用ITestContract.Get方法成功，返回值：" + ret2);

            var proxy3 = new TestContractProxy(remote_endpoint);
            var ret3 = proxy3.GetDecimal(0.01m);
            Console.WriteLine("调用ITestContract.GetDecimal方法成功，返回值：" + ret3);

            var proxy4 = new TestContractProxy(remote_endpoint);
            var ret4 = proxy4.GetId("source", 0.01d, 1, DateTime.Now);
            Console.WriteLine("调用ITestContract.GetId方法成功，返回值：" + ret4);

            var proxy5 = new TestContractProxy(remote_endpoint);
            var ret5 = proxy5.GetItems(Guid.NewGuid());
            Console.WriteLine("调用ITestContract.GetItems方法成功，返回值：" + ret5);

            var proxy6 = new TestContractProxy(remote_endpoint);
            var ret6 = proxy6.OutDecimal(0.01m);
            Console.WriteLine("调用ITestContract.OutDecimal方法成功，返回值：" + ret6);

            var proxy7 = new TestContractProxy(remote_endpoint);
            var ret7 = proxy7.TestLong(1, new List<long> { 0, 1, 2, 3 });
            Console.WriteLine("调用ITestContract.TestLong方法成功，返回值：" + ret7);

            Console.Read();
        }
    }
}
