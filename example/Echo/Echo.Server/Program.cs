using System;

namespace Echo.Server
{
    class Program
    {
        static void Main(string[] args)
        {
            var server = new EchoServer("127.0.0.1", 5001);
            server.Start();

            Console.WriteLine("按任意键关闭server...");
            Console.ReadLine();
            server.Stop();

            Console.Read();

            Console.WriteLine("Hello World!");
        }
    }
}
