using NetGear.Core;
using System;
using System.Buffers;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace ReverseClient
{
    class Program
    {
        static async Task Main(string[] args)
        {
            var endPoint = new IPEndPoint(IPAddress.Loopback, 5000);

            // 开启client
            using (var client = await DuplexPipeClient.ConnectAsync(new ListenOptions(endPoint)))
            {
                // subscribe to broadcasts
                client.Broadcast += async msg =>
                {
                    if (!msg.Memory.IsEmpty)
                        await WriteLineAsync("*", msg);
                };

                string line;
                while ((line = await Console.In.ReadLineAsync()) != null)
                {
                    if (line == "q")
                        break;

                    using (var leased = line.EncodeWithOwnership())
                    {
                        var response = await client.SendReceiveAsync(leased.Memory);
                        await WriteLineAsync("<", response);
                    }
                }
            }
        }

        static async Task WriteLineAsync(string prefix, IMemoryOwner<byte> message)
        {
            await Console.Out.WriteLineAsync($"{prefix} {Encoding.UTF8.GetString(message.Memory.Span)}");
        }
    }
}
