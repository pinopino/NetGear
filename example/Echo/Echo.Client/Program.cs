using NetGear.Core.Client;
using System;
using System.Threading;

namespace Echo.Client
{
    class Program
    {
        static void Main(string[] args)
        {
            using (var conn = new StreamedSocketClientConnection(1, "127.0.0.1", 5001, 256))
            {
                conn.Connect();
                while (true)
                {
                    Console.WriteLine("发送消息");
                    conn.Write("hello world").Wait();
                    var res = conn.ReadString().Result;
                    Console.WriteLine("收到服务端响应：" + res);
                    Thread.Sleep(1000);
                }
            }

            Console.Read();
        }
    }
}
