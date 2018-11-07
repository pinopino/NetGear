using NetGear.Example.Rpc;
using System;
using System.Net;

namespace Rpc.Client
{
    class Program
    {
        static void Main(string[] args)
        {
            var proxy = new DataContractProxy(new IPEndPoint(IPAddress.Parse("127.0.0.1"), 5000));
            var ret = proxy.AddMoney(1, 2);
            Console.WriteLine(ret);

            Console.Read();
        }
    }
}
