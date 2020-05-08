﻿using NetGear.Core;
using System;
using System.Buffers;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace ReverseServer
{
    class Program
    {
        static async Task Main(string[] args)
        {
            var endPoint = new IPEndPoint(IPAddress.Loopback, 5000);

            // 开启server
            using (var server = new Server())
            {
                server.Start(endPoint);

                string line;
                while ((line = await Console.In.ReadLineAsync()) != null)
                {
                    if (line == "q")
                        break;

                    int clientCount, len;
                    using (var leased = Encode(line))
                    {
                        len = leased.Memory.Length;
                        clientCount = await server.BroadcastAsync(leased.Memory);
                    }
                    await Console.Out.WriteLineAsync(
                        $"Broadcast {len} bytes to {clientCount} clients");
                }
            }

            // 开启client
            using (var client = await SimplPipelineClient.ConnectAsync(endPoint))
            {
                // subscribe to broadcasts
                client.OnBroadcast += async msg =>
                {
                    if (!msg.Memory.IsEmpty)
                        await WriteLineAsync("*", msg);
                };

                string line;
                while ((line = await Console.In.ReadLineAsync()) != null)
                {
                    if (line == "q")
                        break;

                    using (var leased = Encode(line))
                    {
                        var response = await client.SendReceiveAsync(leased.Memory);
                        await WriteLineAsync("<", response);
                    }
                }
            }
        }

        static IMemoryOwner<byte> Encode(string line)
        {
            var origin = Encoding.UTF8.GetBytes(line);
            var bytes = ArrayPool<byte>.Shared.Rent(origin.Length);
            Array.Copy(origin, bytes, origin.Length);

            return new ArrayPoolOwner<byte>(bytes, bytes.Length);
        }

        static async Task WriteLineAsync(string prefix, IMemoryOwner<byte> message)
        {
            await Console.Out.WriteLineAsync($"{prefix} {Encoding.UTF8.GetString(message.Memory.Span)}");
        }
    }
}