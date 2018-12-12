using NetGear.Core.Client;
using System;
using System.Threading;

namespace Echo.Client
{
    class Program
    {
        static void Main(string[] args)
        {
            using (var conn = new EAPStreamedClientConnection(1, "127.0.0.1", 5001, 256))
            {
                conn.Connect();
                while (true)
                {
                    Console.WriteLine("发送消息");
                    var bytes = System.Text.Encoding.UTF8.GetBytes("hello world!");
                    conn.BeginWrite(bytes, 0, bytes.Length, false);
                    Thread.Sleep(1000);
                }
            }

            Console.Read();
        }
    }
}
